using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class ItemImageService : IItemImageService
{
    private readonly AppDbContext _db;

    public ItemImageService(AppDbContext db) => _db = db;

    public async Task<ItemImageDto?> GetAsync(string kind, Guid ownerId, CancellationToken ct = default)
    {
        kind = Normalize(kind);
        var img = await _db.ItemImages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Kind == kind && x.OwnerId == ownerId, ct);
        return img is null ? null : new ItemImageDto(img.Kind, img.OwnerId, img.ContentType, img.Data);
    }

    public async Task<IReadOnlyList<ItemImageDto>> ListAsync(CancellationToken ct = default)
        => await _db.ItemImages.AsNoTracking()
            .Select(x => new ItemImageDto(x.Kind, x.OwnerId, x.ContentType, x.Data))
            .ToListAsync(ct);

    public async Task SetAsync(string kind, Guid ownerId, byte[] data, string contentType, CancellationToken ct = default)
    {
        if (data is null || data.Length == 0)
            throw new ArgumentException("Image data is empty.", nameof(data));

        kind = Normalize(kind);
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "image/jpeg";

        var existing = await _db.ItemImages
            .FirstOrDefaultAsync(x => x.Kind == kind && x.OwnerId == ownerId, ct);

        if (existing is null)
        {
            _db.ItemImages.Add(new ItemImage
            {
                Kind = kind,
                OwnerId = ownerId,
                Data = data,
                ContentType = contentType,
                UpdatedUtc = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Data = data;
            existing.ContentType = contentType;
            existing.UpdatedUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task RemoveAsync(string kind, Guid ownerId, CancellationToken ct = default)
    {
        kind = Normalize(kind);
        return _db.ItemImages
            .Where(x => x.Kind == kind && x.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);
    }

    private static string Normalize(string kind) => (kind ?? string.Empty).Trim().ToLowerInvariant();
}
