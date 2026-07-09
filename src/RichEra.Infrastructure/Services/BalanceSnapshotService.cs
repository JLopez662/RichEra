using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class BalanceSnapshotService : IBalanceSnapshotService
{
    private readonly AppDbContext _db;
    public BalanceSnapshotService(AppDbContext db) => _db = db;

    public async Task RecordAsync(decimal totalBalance, DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);
        var existing = await _db.BalanceSnapshots.FirstOrDefaultAsync(s => s.Date == day, ct);
        if (existing is null)
        {
            _db.BalanceSnapshots.Add(new BalanceSnapshot { Date = day, TotalBalance = totalBalance });
        }
        else
        {
            if (existing.TotalBalance == totalBalance) return;   // nothing changed — skip the write
            existing.TotalBalance = totalBalance;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BalanceTrendPointDto>> GetTrendAsync(int days = 90, CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(DateTime.Today).AddDays(-Math.Max(1, days));
        return await _db.BalanceSnapshots.AsNoTracking()
            .Where(s => s.Date >= from)
            .OrderBy(s => s.Date)
            .Select(s => new BalanceTrendPointDto(s.Date, s.TotalBalance))
            .ToListAsync(ct);
    }
}
