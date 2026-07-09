using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;
    public CategoryService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CategoryKind? kind = null, bool includeArchived = false, CancellationToken ct = default)
    {
        var q = _db.Categories.AsNoTracking().AsQueryable();
        if (kind is CategoryKind k) q = q.Where(c => c.Kind == k);
        if (!includeArchived) q = q.Where(c => !c.IsArchived);
        return await q.OrderBy(c => c.Kind).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Kind, c.Icon, c.ColorHex, c.IsArchived))
            .ToListAsync(ct);
    }

    public async Task<CategoryDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new CategoryDto(c.Id, c.Name, c.Kind, c.Icon, c.ColorHex, c.IsArchived);
    }

    public async Task<CategoryDto> UpsertAsync(CategoryUpsertDto dto, CancellationToken ct = default)
    {
        Category entity;
        if (dto.Id is Guid id)
        {
            entity = await _db.Categories.FirstAsync(c => c.Id == id, ct);
            entity.Name = dto.Name;
            entity.Kind = dto.Kind;
            entity.Icon = dto.Icon;
            entity.ColorHex = dto.ColorHex;
        }
        else
        {
            entity = new Category
            {
                Name = dto.Name,
                Kind = dto.Kind,
                Icon = dto.Icon,
                ColorHex = dto.ColorHex,
            };
            _db.Categories.Add(entity);
        }
        await _db.SaveChangesAsync(ct);
        return new CategoryDto(entity.Id, entity.Name, entity.Kind, entity.Icon, entity.ColorHex, entity.IsArchived);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Categories.FirstAsync(c => c.Id == id, ct);
        entity.IsArchived = true;
        await _db.SaveChangesAsync(ct);
    }
}
