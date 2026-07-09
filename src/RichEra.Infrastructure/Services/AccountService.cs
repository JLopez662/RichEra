using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly AppDbContext _db;
    public AccountService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<AccountDto>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var q = _db.Accounts.AsNoTracking().AsQueryable();
        if (!includeArchived) q = q.Where(a => !a.IsArchived);
        return await q.OrderBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.Name, a.Type, a.OpeningBalance, a.CurrentBalance, a.IsArchived))
            .ToListAsync(ct);
    }

    public async Task<AccountDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var a = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : new AccountDto(a.Id, a.Name, a.Type, a.OpeningBalance, a.CurrentBalance, a.IsArchived);
    }

    public async Task<AccountDto> UpsertAsync(AccountUpsertDto dto, CancellationToken ct = default)
    {
        Account entity;
        if (dto.Id is Guid id)
        {
            entity = await _db.Accounts.FirstAsync(a => a.Id == id, ct);
            var balanceDelta = dto.OpeningBalance - entity.OpeningBalance;
            entity.Name = dto.Name;
            entity.Type = dto.Type;
            entity.OpeningBalance = dto.OpeningBalance;
            entity.CurrentBalance += balanceDelta;
        }
        else
        {
            entity = new Account
            {
                Name = dto.Name,
                Type = dto.Type,
                OpeningBalance = dto.OpeningBalance,
                CurrentBalance = dto.OpeningBalance,
            };
            _db.Accounts.Add(entity);
        }
        await _db.SaveChangesAsync(ct);
        return new AccountDto(entity.Id, entity.Name, entity.Type, entity.OpeningBalance, entity.CurrentBalance, entity.IsArchived);
    }

    public async Task<AccountDto> SetBalanceAsync(SetBalanceDto dto, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstAsync(a => a.Id == dto.AccountId, ct);
        var delta = dto.NewBalance - account.CurrentBalance;

        // Optionally leave an audit trail so the transaction history still reconciles to the
        // balance. The balance is set absolutely below, so this row explains the jump rather
        // than being double-counted. A zero-delta change records nothing.
        if (dto.RecordAdjustment && delta != 0m)
        {
            _db.Transactions.Add(new Transaction
            {
                Date = DateOnly.FromDateTime(DateTime.Today),
                AccountId = account.Id,
                Amount = Math.Abs(delta),
                Direction = delta > 0 ? TransactionDirection.Income : TransactionDirection.Expense,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? "Balance adjustment" : dto.Note,
                // Not a real-world transaction: excluded from cash-flow stats and from
                // statement-import matching (the bank will never post a row for it).
                ImportSource = "reconcile",
            });
        }

        account.CurrentBalance = dto.NewBalance;
        await _db.SaveChangesAsync(ct);
        return new AccountDto(account.Id, account.Name, account.Type, account.OpeningBalance, account.CurrentBalance, account.IsArchived);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Accounts.FirstAsync(a => a.Id == id, ct);
        entity.IsArchived = true;
        await _db.SaveChangesAsync(ct);
    }
}
