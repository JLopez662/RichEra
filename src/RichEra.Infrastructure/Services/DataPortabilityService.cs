using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class DataPortabilityService : IDataPortabilityService
{
    private readonly AppDbContext _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public DataPortabilityService(AppDbContext db) => _db = db;

    public async Task<string> ExportAsJsonAsync(CancellationToken ct = default)
    {
        var export = new AppDataExport
        {
            Schema = AppDataExport.CurrentSchema,
            ExportedAtUtc = DateTime.UtcNow,
            Database = new AppDatabaseSection
            {
                Accounts     = await _db.Accounts.AsNoTracking().ToListAsync(ct),
                Categories   = await _db.Categories.AsNoTracking().ToListAsync(ct),
                Items        = await _db.Items.AsNoTracking().ToListAsync(ct),
                Transactions = await _db.Transactions.AsNoTracking().ToListAsync(ct),
                ItemImages   = await _db.ItemImages.AsNoTracking().ToListAsync(ct),
                Settings     = await _db.Settings.AsNoTracking().ToListAsync(ct),
                Bundles      = await _db.Bundles.AsNoTracking().ToListAsync(ct),
                BundleLines  = await _db.BundleLines.AsNoTracking().ToListAsync(ct),
                BalanceSnapshots = await _db.BalanceSnapshots.AsNoTracking().ToListAsync(ct),
            },
        };

        return JsonSerializer.Serialize(export, JsonOptions);
    }

    public async Task<DataImportResult> ImportFromJsonAsync(string json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DataImportResult.Failed("The backup file is empty.");

        AppDataExport? payload;
        try
        {
            payload = Deserialize(json);
        }
        catch (Exception ex)
        {
            return DataImportResult.Failed($"Couldn't parse the backup file: {ex.Message}");
        }

        if (payload is null)
            return DataImportResult.Failed("The backup file produced an empty payload.");
        if (!string.Equals(payload.Schema, AppDataExport.CurrentSchema, StringComparison.Ordinal))
            return DataImportResult.Failed(
                $"Unsupported backup format '{payload.Schema}'. This build can restore " +
                $"'{AppDataExport.CurrentSchema}' (and upgrades '{AppDataExport.LegacySchemaV1}' automatically).");

        var data = payload.Database;

        // Full replace inside one transaction: if anything past this point throws, the rollback
        // restores the pre-import state so a bad backup can't leave a half-loaded database.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Wipe child -> parent so foreign-key restrictions are never violated mid-clear
            // (Transaction and Item both reference Account with DeleteBehavior.Restrict).
            await _db.Transactions.ExecuteDeleteAsync(ct);
            await _db.Items.ExecuteDeleteAsync(ct);
            await _db.Categories.ExecuteDeleteAsync(ct);
            await _db.Accounts.ExecuteDeleteAsync(ct);
            await _db.BundleLines.ExecuteDeleteAsync(ct);     // child of Bundles
            await _db.Bundles.ExecuteDeleteAsync(ct);
            await _db.ItemImages.ExecuteDeleteAsync(ct);      // no FK; order is irrelevant
            await _db.Settings.ExecuteDeleteAsync(ct);        // no FK; order is irrelevant
            await _db.BalanceSnapshots.ExecuteDeleteAsync(ct); // no FK; order is irrelevant

            // ExecuteDelete bypasses the change tracker, so entities loaded earlier in this
            // (long-lived Blazor) scope are still tracked under their old ids. Detach them or
            // re-adding rows that share those ids throws "instance ... is already being tracked".
            _db.ChangeTracker.Clear();

            // Insert parent -> child so every foreign-key target already exists.
            _db.Accounts.AddRange(data.Accounts);
            _db.Categories.AddRange(data.Categories);
            _db.Items.AddRange(data.Items);
            _db.Transactions.AddRange(data.Transactions);
            _db.ItemImages.AddRange(data.ItemImages);
            _db.Settings.AddRange(data.Settings);
            // A serialized Bundle may embed its lines; adding both the graph and the flat
            // BundleLines list would double them, so strip embedded lines and trust the flat list.
            foreach (var bundle in data.Bundles) bundle.Lines.Clear();
            _db.Bundles.AddRange(data.Bundles);
            _db.BundleLines.AddRange(data.BundleLines);
            _db.BalanceSnapshots.AddRange(data.BalanceSnapshots);
            await _db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return DataImportResult.Succeeded(
                "Restore complete.",
                new ImportCounts(
                    data.Accounts.Count,
                    data.Categories.Count,
                    data.Items.Count,
                    data.Transactions.Count));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            return DataImportResult.Failed($"Restore failed: {ex.Message}. Your existing data was kept.");
        }
    }

    // Deserialize a backup of any supported vintage into the current shape.
    private static AppDataExport? Deserialize(string json)
    {
        var payload = JsonSerializer.Deserialize<AppDataExport>(json, JsonOptions);
        if (payload is not null && string.Equals(payload.Schema, AppDataExport.LegacySchemaV1, StringComparison.Ordinal))
            payload = JsonSerializer.Deserialize<AppDataExport>(UpgradeV1Json(json), JsonOptions);
        return payload;
    }

    // v1 -> v2 in JSON space, so old auto-backups keep restoring after the Money Log rename:
    //   database.incomeSources        -> database.items   (fields are a strict subset of Item's)
    //   transactions[].incomeSourceId -> itemId           (quantity defaults to 1 when absent)
    // Every other v1 field name survived the rename unchanged.
    private static string UpgradeV1Json(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("The backup file is not a JSON object.");
        root[FindKey(root, "schema") ?? "schema"] = AppDataExport.CurrentSchema;

        if (FindKey(root, "database") is string dbKey && root[dbKey] is JsonObject db)
        {
            if (FindKey(db, "incomeSources") is string srcKey)
            {
                var items = db[srcKey];
                db.Remove(srcKey);
                db["items"] = items;
            }
            if (FindKey(db, "transactions") is string txKey && db[txKey] is JsonArray txns)
            {
                foreach (var node in txns)
                {
                    if (node is not JsonObject t) continue;
                    if (FindKey(t, "incomeSourceId") is not string fkKey) continue;
                    var v = t[fkKey];
                    t.Remove(fkKey);
                    t["itemId"] = v;
                }
            }
        }

        return root.ToJsonString();
    }

    // Backups written by this app use camelCase keys, but hand-edited files may differ in case.
    private static string? FindKey(JsonObject obj, string name)
    {
        foreach (var kv in obj)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        return null;
    }
}
