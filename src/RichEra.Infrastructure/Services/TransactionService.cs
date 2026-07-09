using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class TransactionService : ITransactionService
{
    private readonly AppDbContext _db;
    public TransactionService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransactionDto>> ListAsync(int year, int month, Guid? accountId = null, CancellationToken ct = default)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        var q = _db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Date >= from && t.Date < to);
        if (accountId is Guid aid) q = q.Where(t => t.AccountId == aid);

        var result = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedUtc)
            .Select(t => new TransactionDto(
                t.Id, t.Date, t.AccountId, t.Account!.Name,
                t.CategoryId, t.Category != null ? t.Category.Name : null,
                t.Amount, t.Direction, t.Note, t.TransferGroupId,
                t.CreatedUtc, t.ExternalId, t.ImportSource, t.ItemId, t.Quantity,
                t.Item != null && t.Item.Bucket != null ? t.Item.Bucket
                    : t.Category != null ? t.Category.Bucket : null,
                t.Item != null ? t.Item.Name : null,
                t.Item != null ? t.Item.UnitName : null))
            .ToListAsync(ct);

        Debug.WriteLine($"[Txns] ListAsync window={from:yyyy-MM-dd}..{to:yyyy-MM-dd} " +
                        $"account={accountId?.ToString() ?? "all"} -> {result.Count} rows");
        return result;
    }

    public async Task<IReadOnlyList<TransactionDto>> SearchAsync(string query, int limit = 300, CancellationToken ct = default)
    {
        var tokens = (query ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
        if (tokens.Length == 0) return Array.Empty<TransactionDto>();

        var q = _db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .AsQueryable();

        // AND across tokens: each must appear in the note, category, or account name. ToLower()
        // on both sides makes the match case-insensitive (SQLite lower() is ASCII-only, which is
        // fine for the uppercase bank descriptions this searches).
        foreach (var token in tokens)
        {
            var tk = token;
            q = q.Where(t =>
                (t.Note != null && t.Note.ToLower().Contains(tk)) ||
                (t.Category != null && t.Category.Name.ToLower().Contains(tk)) ||
                t.Account!.Name.ToLower().Contains(tk));
        }

        var result = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedUtc)
            .Take(limit)
            .Select(t => new TransactionDto(
                t.Id, t.Date, t.AccountId, t.Account!.Name,
                t.CategoryId, t.Category != null ? t.Category.Name : null,
                t.Amount, t.Direction, t.Note, t.TransferGroupId,
                t.CreatedUtc, t.ExternalId, t.ImportSource, t.ItemId, t.Quantity,
                t.Item != null && t.Item.Bucket != null ? t.Item.Bucket
                    : t.Category != null ? t.Category.Bucket : null,
                t.Item != null ? t.Item.Name : null,
                t.Item != null ? t.Item.UnitName : null))
            .ToListAsync(ct);

        Debug.WriteLine($"[Txns] SearchAsync '{query}' tokens={tokens.Length} -> {result.Count} rows (cap {limit})");
        return result;
    }

    public async Task<IReadOnlyList<(int Year, int Month)>> GetActiveMonthsAsync(CancellationToken ct = default)
    {
        var months = await _db.Transactions.AsNoTracking()
            .Select(t => new { t.Date.Year, t.Date.Month })
            .Distinct()
            .ToListAsync(ct);
        return months
            .Select(m => (m.Year, m.Month))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToList();
    }

    public async Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.Transactions.AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Category)
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : new TransactionDto(
            t.Id, t.Date, t.AccountId, t.Account!.Name,
            t.CategoryId, t.Category?.Name,
            t.Amount, t.Direction, t.Note, t.TransferGroupId,
            t.CreatedUtc, t.ExternalId, t.ImportSource, t.ItemId, t.Quantity,
            t.Item?.Bucket ?? t.Category?.Bucket, t.Item?.Name, t.Item?.UnitName);
    }

    public async Task<TransactionDto> UpsertAsync(TransactionUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(dto));

        Transaction entity;
        decimal balanceDelta;
        Guid oldAccountId = Guid.Empty;
        decimal oldSignedAmount = 0m;

        if (dto.Id is Guid id)
        {
            entity = await _db.Transactions.FirstAsync(x => x.Id == id, ct);
            if (entity.TransferGroupId is not null)
                throw new InvalidOperationException("Edit transfers via the transfer flow, not the transaction flow.");

            oldAccountId = entity.AccountId;
            oldSignedAmount = entity.Direction == TransactionDirection.Income ? entity.Amount : -entity.Amount;

            entity.Date = dto.Date;
            entity.AccountId = dto.AccountId;
            entity.CategoryId = dto.CategoryId;
            entity.Amount = dto.Amount;
            entity.Direction = dto.Direction;
            entity.Note = dto.Note;
            // Item linkage is deliberately NOT editable here (recurrence/quick-log flows own it);
            // quantity follows the edit so a corrected row keeps its serving count honest.
            entity.Quantity = dto.Quantity > 0m ? dto.Quantity : entity.Quantity;
        }
        else
        {
            entity = new Transaction
            {
                Date = dto.Date,
                AccountId = dto.AccountId,
                CategoryId = dto.CategoryId,
                Amount = dto.Amount,
                Direction = dto.Direction,
                Note = dto.Note,
                ItemId = dto.ItemId,
                Quantity = dto.Quantity > 0m ? dto.Quantity : 1m,
            };
            _db.Transactions.Add(entity);
        }

        var newSignedAmount = dto.Direction == TransactionDirection.Income ? dto.Amount : -dto.Amount;
        balanceDelta = newSignedAmount;

        if (dto.Id is not null && oldAccountId == dto.AccountId)
        {
            var account = await _db.Accounts.FirstAsync(a => a.Id == dto.AccountId, ct);
            account.CurrentBalance += (newSignedAmount - oldSignedAmount);
        }
        else
        {
            if (dto.Id is not null)
            {
                var oldAccount = await _db.Accounts.FirstAsync(a => a.Id == oldAccountId, ct);
                oldAccount.CurrentBalance -= oldSignedAmount;
            }
            var newAccount = await _db.Accounts.FirstAsync(a => a.Id == dto.AccountId, ct);
            newAccount.CurrentBalance += newSignedAmount;
        }

        await _db.SaveChangesAsync(ct);

        var loaded = await _db.Transactions.AsNoTracking()
            .Include(x => x.Account)
            .Include(x => x.Category)
            .Include(x => x.Item)
            .FirstAsync(x => x.Id == entity.Id, ct);
        return new TransactionDto(
            loaded.Id, loaded.Date, loaded.AccountId, loaded.Account!.Name,
            loaded.CategoryId, loaded.Category?.Name,
            loaded.Amount, loaded.Direction, loaded.Note, loaded.TransferGroupId,
            loaded.CreatedUtc, loaded.ExternalId, loaded.ImportSource, loaded.ItemId, loaded.Quantity,
            loaded.Item?.Bucket ?? loaded.Category?.Bucket, loaded.Item?.Name, loaded.Item?.UnitName);
    }

    public async Task CreateTransferAsync(TransferUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(dto));
        if (dto.FromAccountId == dto.ToAccountId) throw new ArgumentException("Transfer source and destination must differ.");

        var fromAccount = await _db.Accounts.FirstAsync(a => a.Id == dto.FromAccountId, ct);
        var toAccount = await _db.Accounts.FirstAsync(a => a.Id == dto.ToAccountId, ct);

        var groupId = Guid.NewGuid();
        var outLeg = new Transaction
        {
            Date = dto.Date,
            AccountId = dto.FromAccountId,
            Amount = dto.Amount,
            Direction = TransactionDirection.Expense,
            Note = dto.Note,
            TransferGroupId = groupId,
        };
        var inLeg = new Transaction
        {
            Date = dto.Date,
            AccountId = dto.ToAccountId,
            Amount = dto.Amount,
            Direction = TransactionDirection.Income,
            Note = dto.Note,
            TransferGroupId = groupId,
        };
        _db.Transactions.Add(outLeg);
        _db.Transactions.Add(inLeg);

        fromAccount.CurrentBalance -= dto.Amount;
        toAccount.CurrentBalance += dto.Amount;

        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Transactions.FirstAsync(t => t.Id == id, ct);
        if (entity.TransferGroupId is Guid groupId)
        {
            var legs = await _db.Transactions.Where(t => t.TransferGroupId == groupId).ToListAsync(ct);
            foreach (var leg in legs)
            {
                var account = await _db.Accounts.FirstAsync(a => a.Id == leg.AccountId, ct);
                var signed = leg.Direction == TransactionDirection.Income ? leg.Amount : -leg.Amount;
                account.CurrentBalance -= signed;
                _db.Transactions.Remove(leg);
            }
        }
        else
        {
            var account = await _db.Accounts.FirstAsync(a => a.Id == entity.AccountId, ct);
            var signed = entity.Direction == TransactionDirection.Income ? entity.Amount : -entity.Amount;
            account.CurrentBalance -= signed;
            _db.Transactions.Remove(entity);
        }

        // Unwind any "move to savings" transfer this paycheck spawned, reversing its balances.
        var savingsLegs = await _db.Transactions.Where(t => t.SavingsSourceTransactionId == id).ToListAsync(ct);
        foreach (var leg in savingsLegs)
        {
            var acct = await _db.Accounts.FirstAsync(a => a.Id == leg.AccountId, ct);
            acct.CurrentBalance -= leg.Direction == TransactionDirection.Income ? leg.Amount : -leg.Amount;
            _db.Transactions.Remove(leg);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid> MakeRecurringAsync(Guid transactionId, RepeatFrequency frequency, CancellationToken ct = default)
    {
        var t = await _db.Transactions.Include(x => x.Category).FirstAsync(x => x.Id == transactionId, ct);
        if (t.TransferGroupId is not null)
            throw new InvalidOperationException("Transfers can't be made recurring.");

        // Name the rule after the category, falling back to the (trimmed) note, then a generic label.
        var name = !string.IsNullOrWhiteSpace(t.Category?.Name) ? t.Category!.Name
                 : !string.IsNullOrWhiteSpace(t.Note) ? Truncate(t.Note!, 60)!
                 : t.Direction == TransactionDirection.Income ? "Recurring income" : "Recurring expense";

        var rule = new Item
        {
            Name = name,
            Amount = t.Amount,
            Direction = t.Direction,
            Frequency = frequency,
            StartDate = t.Date,         // this transaction's date is occurrence #1
            AccountId = t.AccountId,
            CategoryId = t.CategoryId,
        };
        _db.Items.Add(rule);

        // Link the transaction so the projection for StartDate resolves to "received" (matched by
        // ItemId + Date) instead of projecting a duplicate. Balance is intentionally untouched.
        t.ItemId = rule.Id;
        await _db.SaveChangesAsync(ct);
        return rule.Id;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
