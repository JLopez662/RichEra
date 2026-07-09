using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface IBundleService
{
    /// <summary>Active bundles, newest first, lines resolved against live data.</summary>
    Task<IReadOnlyList<BundleDto>> ListAsync(CancellationToken ct = default);
    Task<BundleDto?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Save a cart as a named bundle. Lines are stored by (kind, ownerId) soft
    /// reference with a title snapshot, so the bundle survives renames and archivals.</summary>
    Task<BundleDto> CreateAsync(string name, IReadOnlyList<BundleLineInput> lines, CancellationToken ct = default);

    Task ArchiveAsync(Guid id, CancellationToken ct = default);
}
