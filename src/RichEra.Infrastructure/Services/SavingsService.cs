using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class SavingsService : ISavingsService
{
    private readonly AppDbContext _db;
    public SavingsService(AppDbContext db) => _db = db;

    public async Task<SavingsGoalDto?> GetGoalAsync(CancellationToken ct = default)
    {
        var g = await ActiveGoalQuery().FirstOrDefaultAsync(ct);
        return g is null ? null : ToDto(g);
    }

    public async Task<SavingsGoalDto> UpsertGoalAsync(SavingsGoalUpsertDto dto, CancellationToken ct = default)
    {
        if (dto.TargetAmount <= 0) throw new ArgumentException("Target must be greater than zero.", nameof(dto));
        if (dto.Mode == SavingsMode.MoveToSavings)
        {
            if (dto.LinkedAccountId is not Guid linkedId)
                throw new ArgumentException("Pick a savings account for move mode.", nameof(dto));
            var ok = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == linkedId && !a.IsArchived, ct);
            if (!ok) throw new ArgumentException("Pick an active savings account.", nameof(dto));
        }

        SavingsGoal entity;
        if (dto.Id is Guid id)
        {
            entity = await _db.SavingsGoals.FirstAsync(x => x.Id == id, ct);
            entity.Name = dto.Name;
            entity.TargetAmount = dto.TargetAmount;
            entity.Mode = dto.Mode;
            entity.LinkedAccountId = dto.Mode == SavingsMode.MoveToSavings ? dto.LinkedAccountId : null;
        }
        else
        {
            entity = new SavingsGoal
            {
                Name = dto.Name,
                TargetAmount = dto.TargetAmount,
                Mode = dto.Mode,
                LinkedAccountId = dto.Mode == SavingsMode.MoveToSavings ? dto.LinkedAccountId : null,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
            };
            _db.SavingsGoals.Add(entity);
        }
        await _db.SaveChangesAsync(ct);

        var saved = await _db.SavingsGoals.AsNoTracking().Include(x => x.LinkedAccount)
            .FirstAsync(x => x.Id == entity.Id, ct);
        return ToDto(saved);
    }

    public async Task ClearGoalAsync(CancellationToken ct = default)
    {
        var goals = await _db.SavingsGoals.Where(x => !x.IsArchived).ToListAsync(ct);
        foreach (var g in goals) g.IsArchived = true;
        if (goals.Count > 0) await _db.SaveChangesAsync(ct);
    }

    public async Task SetSavingsPercentAsync(Guid itemId, int percent, CancellationToken ct = default)
    {
        var src = await _db.Items.FirstAsync(s => s.Id == itemId, ct);
        src.SavingsPercent = Math.Clamp(percent, 0, 100);
        await _db.SaveChangesAsync(ct);
    }

    public async Task PostDepositAsync(Guid goalId, decimal amount, DateOnly date, Guid? fromAccountId = null, CancellationToken ct = default)
    {
        if (amount <= 0m) throw new ArgumentException("Deposit must be greater than zero.", nameof(amount));

        var goal = await _db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == goalId && !g.IsArchived, ct)
            ?? throw new InvalidOperationException("That savings goal is no longer available.");

        if (goal.Mode == SavingsMode.MoveToSavings)
        {
            if (goal.LinkedAccountId is not Guid linkedId)
                throw new InvalidOperationException("This goal has no linked savings account.");
            if (fromAccountId is not Guid fromId)
                throw new ArgumentException("Choose an account to move the money from.", nameof(fromAccountId));
            if (fromId == linkedId)
                throw new ArgumentException("The source and savings accounts must be different.", nameof(fromAccountId));

            var from = await _db.Accounts.FirstAsync(a => a.Id == fromId, ct);
            var linked = await _db.Accounts.FirstAsync(a => a.Id == linkedId, ct);

            // Real money move: a two-leg transfer mirroring SyncSavingsMoveAsync, but NOT tied to a
            // paycheck — SavingsSourceTransactionId stays null so paycheck re-sync / delete-unwind
            // never touches this manual deposit. Only the Income leg is summed for goal progress.
            var groupId = Guid.NewGuid();
            var note = "Savings → " + goal.Name;
            _db.Transactions.Add(new Transaction { Date = date, AccountId = fromId, Amount = amount, Direction = TransactionDirection.Expense, Note = note, TransferGroupId = groupId, SavingsGoalId = goal.Id });
            _db.Transactions.Add(new Transaction { Date = date, AccountId = linkedId, Amount = amount, Direction = TransactionDirection.Income, Note = note, TransferGroupId = groupId, SavingsGoalId = goal.Id });
            from.CurrentBalance -= amount;
            linked.CurrentBalance += amount;
        }
        else
        {
            // Track only: no account holds this goal's money, so a deposit is a balance-neutral
            // tally bump. GetPlanAsync's TrackOnly branch adds these on top of the paycheck %.
            _db.SavingsContributions.Add(new SavingsContribution { GoalId = goal.Id, Amount = amount, Date = date });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<SavingsPlanDto> GetPlanAsync(CancellationToken ct = default)
    {
        var goal = await ActiveGoalQuery().FirstOrDefaultAsync(ct);

        // Scheduled income items that contribute a savings % (an ad-hoc income has no cadence
        // to project a per-month figure from, so it can't take part in the plan).
        var rules = await _db.Items.AsNoTracking()
            .Where(s => !s.IsArchived && s.Direction == TransactionDirection.Income
                        && s.SavingsPercent > 0 && s.Frequency != null)
            .Select(s => new { s.Id, s.Name, s.Amount, s.Frequency, s.SavingsPercent })
            .ToListAsync(ct);

        var contributors = rules
            .OrderByDescending(r => r.Amount * r.SavingsPercent)
            .Select(r => new SavingsContributorDto(
                r.Id, r.Name, r.SavingsPercent,
                Math.Round(r.Amount * r.SavingsPercent / 100m, 2), r.Frequency!.Value))
            .ToList();

        // Normalize each contribution to a per-month figure for the projection.
        var perMonth = Math.Round(
            rules.Sum(r => Math.Round(r.Amount * r.SavingsPercent / 100m, 2) * MonthlyFactor(r.Frequency!.Value)), 2);

        if (goal is null)
            return new SavingsPlanDto(false, null, 0m, 0m, 0, perMonth, null, contributors);

        var start = goal.StartDate;
        decimal saved;
        if (goal.Mode == SavingsMode.MoveToSavings)
        {
            // Money actually moved for THIS goal — legs are stamped with the goal id, so progress
            // is unambiguous across archive/recreate and reused savings accounts.
            var moved = await _db.Transactions.AsNoTracking()
                .Where(t => t.SavingsGoalId == goal.Id && t.Direction == TransactionDirection.Income)
                .Select(t => t.Amount)
                .ToListAsync(ct);
            saved = moved.Sum();
        }
        else
        {
            // Track only: tally each received paycheck × its saved % since the goal's start date.
            var pctById = rules.ToDictionary(r => r.Id, r => r.SavingsPercent);
            var ruleIds = rules.Select(r => r.Id).ToList();
            var received = await _db.Transactions.AsNoTracking()
                .Where(t => t.ItemId != null && ruleIds.Contains(t.ItemId.Value)
                            && t.Direction == TransactionDirection.Income && t.Date >= start)
                .Select(t => new { Sid = t.ItemId!.Value, t.Amount })
                .ToListAsync(ct);
            var notional = Math.Round(received.Sum(t => t.Amount * pctById[t.Sid] / 100m), 2);

            // Plus any one-off manual deposits recorded against this goal.
            var manual = await _db.SavingsContributions.AsNoTracking()
                .Where(c => c.GoalId == goal.Id)
                .SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;

            saved = notional + manual;
        }

        var remaining = Math.Max(0m, goal.TargetAmount - saved);
        var pct = goal.TargetAmount > 0
            ? (int)Math.Round(Math.Clamp(saved / goal.TargetAmount * 100m, 0m, 100m))
            : 0;

        DateOnly? projected = null;
        if (remaining > 0m && perMonth > 0m)
        {
            var days = (double)(remaining / perMonth) * 30.44;
            // Cap at ~100 years; beyond that the target is effectively unreachable (and AddDays would overflow).
            if (days <= 36500) projected = DateOnly.FromDateTime(DateTime.Today).AddDays((int)Math.Ceiling(days));
        }

        return new SavingsPlanDto(true, ToDto(goal), saved, remaining, pct, perMonth, projected, contributors);
    }

    private IQueryable<SavingsGoal> ActiveGoalQuery() =>
        _db.SavingsGoals.AsNoTracking().Include(x => x.LinkedAccount)
            .Where(x => !x.IsArchived).OrderBy(x => x.CreatedUtc);

    private static SavingsGoalDto ToDto(SavingsGoal g) =>
        new(g.Id, g.Name, g.TargetAmount, g.Mode, g.LinkedAccountId, g.LinkedAccount?.Name, g.StartDate);

    private static decimal MonthlyFactor(RepeatFrequency f) => f switch
    {
        RepeatFrequency.Weekly => 52m / 12m,
        RepeatFrequency.Biweekly => 26m / 12m,
        _ => 1m,
    };
}
