using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Application.Abstractions;

public interface IItemService
{
    /// <summary>List items, filterable by direction and (for the recurring hub) schedule presence.
    /// scheduledOnly: true → only items with a recurrence; false → only ad-hoc; null → all.</summary>
    Task<IReadOnlyList<ItemDto>> ListAsync(bool includeArchived = false, TransactionDirection? direction = null, bool? scheduledOnly = null, CancellationToken ct = default);
    Task<ItemDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ItemDto> UpsertAsync(ItemUpsertDto dto, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Usage facts from the item's logged transactions (history block + autofill).</summary>
    Task<ItemStatsDto> GetStatsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Most recent log date per item, in one query — recency sort for the Library.</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly>> GetLastLoggedAsync(CancellationToken ct = default);
}
