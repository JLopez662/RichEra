using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Application.Abstractions;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CategoryKind? kind = null, bool includeArchived = false, CancellationToken ct = default);
    Task<CategoryDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CategoryDto> UpsertAsync(CategoryUpsertDto dto, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);
}
