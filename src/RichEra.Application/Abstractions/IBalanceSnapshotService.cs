using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface IBalanceSnapshotService
{
    /// <summary>Record a weigh-in: upsert the day's total balance (last write wins).</summary>
    Task RecordAsync(decimal totalBalance, DateOnly? date = null, CancellationToken ct = default);

    /// <summary>Snapshots inside the trailing window, oldest first — the Home trend line.</summary>
    Task<IReadOnlyList<BalanceTrendPointDto>> GetTrendAsync(int days = 90, CancellationToken ct = default);
}
