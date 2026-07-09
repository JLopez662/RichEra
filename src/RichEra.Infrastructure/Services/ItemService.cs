using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class ItemService : IItemService
{
    private readonly AppDbContext _db;
    public ItemService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ItemDto>> ListAsync(bool includeArchived = false, TransactionDirection? direction = null, bool? scheduledOnly = null, CancellationToken ct = default)
    {
        var q = _db.Items.AsNoTracking()
            .Include(s => s.Account)
            .Include(s => s.Category)
            .AsQueryable();
        if (!includeArchived) q = q.Where(s => !s.IsArchived);
        if (direction is TransactionDirection dir) q = q.Where(s => s.Direction == dir);
        if (scheduledOnly is bool sched) q = sched
            ? q.Where(s => s.Frequency != null)
            : q.Where(s => s.Frequency == null);
        var rows = await q.OrderBy(s => s.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<ItemDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.Items.AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? null : ToDto(s);
    }

    public async Task<ItemDto> UpsertAsync(ItemUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(dto));
        // A schedule is all-or-nothing: a frequency without an anchor date can't project.
        if (dto.Frequency is not null && dto.StartDate is null)
            throw new ArgumentException("A repeating item needs a start date.", nameof(dto));

        Item entity;
        if (dto.Id is Guid id)
        {
            entity = await _db.Items.FirstAsync(s => s.Id == id, ct);
        }
        else
        {
            entity = new Item();
            _db.Items.Add(entity);
        }

        entity.Name = dto.Name;
        entity.Counterparty = NullIfBlank(dto.Counterparty);
        entity.Amount = dto.Amount;
        entity.UnitName = NullIfBlank(dto.UnitName);
        entity.Direction = dto.Direction;
        entity.Bucket = dto.Bucket;
        entity.Frequency = dto.Frequency;
        entity.StartDate = dto.Frequency is null ? null : dto.StartDate;
        entity.AccountId = dto.AccountId;
        entity.CategoryId = dto.CategoryId;
        entity.Icon = dto.Icon;
        entity.SavingsPercent = Math.Clamp(dto.SavingsPercent, 0, 100);
        entity.AprPct = dto.AprPct;
        entity.Interest = dto.Interest;
        entity.PrincipalBalance = dto.PrincipalBalance;
        entity.PlanPaymentsTotal = dto.PlanPaymentsTotal;
        entity.PlanPaymentsMade = dto.PlanPaymentsMade;
        entity.ExpectedReturnPct = dto.ExpectedReturnPct;
        entity.FeesPerUnit = dto.FeesPerUnit;
        entity.IsTaxDeductible = dto.IsTaxDeductible;

        await _db.SaveChangesAsync(ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Items.FirstAsync(s => s.Id == id, ct);
        entity.IsArchived = true;
        await _db.SaveChangesAsync(ct);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ItemDto ToDto(Item s) => new(
        s.Id, s.Name, s.Amount, s.Direction, s.Frequency, s.StartDate,
        s.AccountId, s.Account!.Name,
        s.CategoryId, s.Category?.Name,
        s.Icon, s.IsArchived, s.SavingsPercent,
        s.Counterparty, s.UnitName, s.Bucket,
        s.AprPct, s.Interest, s.PrincipalBalance,
        s.PlanPaymentsTotal, s.PlanPaymentsMade,
        s.ExpectedReturnPct, s.FeesPerUnit, s.IsTaxDeductible);
}
