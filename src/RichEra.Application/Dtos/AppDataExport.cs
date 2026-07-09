using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

// The on-disk shape of a full backup. Versioned so an incompatible future change can be
// detected (and rejected with a clear message) rather than silently mis-imported.
// v2 (Money Log foundations): IncomeSources became Items (optional schedule + micros),
// transactions carry itemId + quantity, categories carry a macro bucket, settings added.
// v1 backups are still restorable — the importer upgrades them on the fly.
public sealed class AppDataExport
{
    public const string CurrentSchema = "richera-export/v2";
    public const string LegacySchemaV1 = "richera-export/v1";

    public string Schema { get; set; } = CurrentSchema;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public AppDatabaseSection Database { get; set; } = new();
}

// Entities are serialized directly. Navigation properties are never loaded (AsNoTracking
// without Include), so they stay null and are omitted from the JSON; the foreign-key Guids
// carry all the relationships needed to rebuild the graph on import.
public sealed class AppDatabaseSection
{
    public List<Account> Accounts { get; set; } = new();
    public List<Category> Categories { get; set; } = new();
    public List<Item> Items { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();

    // Item pictures. Additive and backward-compatible: a backup written before this field
    // existed simply deserializes to an empty list, so old auto-backups still restore cleanly.
    public List<ItemImage> ItemImages { get; set; } = new();

    // App-level knobs (budget overrides etc.). Same additive rule as ItemImages.
    public List<Setting> Settings { get; set; } = new();
}
