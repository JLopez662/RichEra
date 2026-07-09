using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class BundleService : IBundleService
{
    private readonly AppDbContext _db;
    public BundleService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<BundleDto>> ListAsync(CancellationToken ct = default)
    {
        var bundles = await _db.Bundles.AsNoTracking()
            .Include(b => b.Lines)
            .Where(b => !b.IsArchived)
            .OrderByDescending(b => b.CreatedUtc)
            .ToListAsync(ct);
        return await ResolveAsync(bundles, ct);
    }

    public async Task<BundleDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var bundle = await _db.Bundles.AsNoTracking()
            .Include(b => b.Lines)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bundle is null) return null;
        return (await ResolveAsync(new[] { bundle }, ct))[0];
    }

    public async Task<BundleDto> CreateAsync(string name, IReadOnlyList<BundleLineInput> lines, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Give the bundle a name.", nameof(name));
        if (lines.Count == 0) throw new ArgumentException("A bundle needs at least one line.", nameof(lines));

        var bundle = new Bundle { Name = name.Trim() };
        foreach (var l in lines)
        {
            if (l.Amount <= 0m) continue;   // a zero line is noise, not a recipe ingredient
            bundle.Lines.Add(new BundleLine
            {
                BundleId = bundle.Id,
                Kind = l.Kind.ToLowerInvariant(),
                OwnerId = l.OwnerId,
                Amount = l.Amount,
                FromAccountId = l.FromAccountId,
                TitleSnapshot = l.Title.Length <= 140 ? l.Title : l.Title[..140],
            });
        }
        if (bundle.Lines.Count == 0) throw new ArgumentException("A bundle needs at least one line with an amount.", nameof(lines));

        _db.Bundles.Add(bundle);
        await _db.SaveChangesAsync(ct);
        return (await GetAsync(bundle.Id, ct))!;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var bundle = await _db.Bundles.FirstAsync(b => b.Id == id, ct);
        bundle.IsArchived = true;
        await _db.SaveChangesAsync(ct);
    }

    // Resolve every line against live data in three set queries (not per-line lookups):
    // current titles where the target still exists, availability where it's still active.
    private async Task<IReadOnlyList<BundleDto>> ResolveAsync(IReadOnlyList<Bundle> bundles, CancellationToken ct)
    {
        var allLines = bundles.SelectMany(b => b.Lines).ToList();

        var itemIds = Ids(allLines, "expense");
        var accountIds = Ids(allLines, "card");
        var goalIds = Ids(allLines, "savings");
        var fundingIds = allLines.Where(l => l.FromAccountId is not null).Select(l => l.FromAccountId!.Value).Distinct().ToList();

        var items = itemIds.Count == 0 ? new() : await _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => (i.Name, Active: !i.IsArchived), ct);
        var accounts = accountIds.Count == 0 && fundingIds.Count == 0 ? new() : await _db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id) || fundingIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.Name, Active: !a.IsArchived), ct);
        var goals = goalIds.Count == 0 ? new() : await _db.SavingsGoals.AsNoTracking()
            .Where(g => goalIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => (g.Name, Active: !g.IsArchived), ct);

        return bundles.Select(b => new BundleDto(
            b.Id, b.Name, b.CreatedUtc,
            b.Lines.OrderBy(l => l.TitleSnapshot, StringComparer.OrdinalIgnoreCase).Select(l =>
            {
                var live = l.Kind switch
                {
                    "expense" => items.TryGetValue(l.OwnerId, out var i) ? i : default((string, bool)?),
                    "card" => accounts.TryGetValue(l.OwnerId, out var a) ? a : default((string, bool)?),
                    "savings" => goals.TryGetValue(l.OwnerId, out var g) ? g : default((string, bool)?),
                    _ => null,
                };
                var fromName = l.FromAccountId is Guid f && accounts.TryGetValue(f, out var fa) ? fa.Name : null;
                return new BundleLineDto(
                    l.Id, l.Kind, l.OwnerId, l.Amount, l.FromAccountId, fromName,
                    live?.Item1 ?? l.TitleSnapshot,
                    live?.Item2 ?? false);
            }).ToList())).ToList();
    }

    private static List<Guid> Ids(IEnumerable<BundleLine> lines, string kind) =>
        lines.Where(l => l.Kind == kind).Select(l => l.OwnerId).Distinct().ToList();
}
