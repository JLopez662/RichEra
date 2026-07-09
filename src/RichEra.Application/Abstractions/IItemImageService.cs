using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// Stores and retrieves the optional picture attached to a shop item (expense / card / savings).
// `kind` is the lowercased ShopItemKind token ("expense" | "card" | "savings"); implementations
// normalize it. One image per (kind, ownerId).
public interface IItemImageService
{
    Task<ItemImageDto?> GetAsync(string kind, Guid ownerId, CancellationToken ct = default);

    // Every stored image, for callers (the shop grid) that show many thumbnails at once.
    Task<IReadOnlyList<ItemImageDto>> ListAsync(CancellationToken ct = default);

    // Adds or replaces the image for an item. `data` must be non-empty.
    Task SetAsync(string kind, Guid ownerId, byte[] data, string contentType, CancellationToken ct = default);

    Task RemoveAsync(string kind, Guid ownerId, CancellationToken ct = default);
}
