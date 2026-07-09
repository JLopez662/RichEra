using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class IncomeProjectionService : IIncomeProjectionService
{
    private readonly AppDbContext _db;
    public IncomeProjectionService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProjectedPaycheckDto>> GetPaychecksAsync(
        Guid itemId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var src = await _db.Items.AsNoTracking().FirstAsync(s => s.Id == itemId, ct);

        var dates = RecurrenceSchedule.EnumeratePayDates(src, from, to).ToList();
        if (dates.Count == 0) return Array.Empty<ProjectedPaycheckDto>();

        // Actual payments rarely land exactly on the projected date (banks post 1–4 days late),
        // so pull actuals a window wider than the projected range and match tolerantly.
        const int window = TransactionMatcher.OccurrenceWindowDays;
        var minDate = dates.Min().AddDays(-window);
        var maxDate = dates.Max().AddDays(window);
        var actuals = await _db.Transactions.AsNoTracking()
            .Where(t => t.ItemId == itemId && t.Date >= minDate && t.Date <= maxDate)
            .Select(t => new { t.Id, t.Date, t.Amount })
            .ToListAsync(ct);

        // Assign each actual to at most one projected date: exact hits first, then nearest
        // within the window — so a payment posted on the 17th still pays the 15th's occurrence.
        var assigned = new Dictionary<DateOnly, (Guid Id, DateOnly Date, decimal Amount)>();
        var remaining = actuals.OrderBy(a => a.Date).ThenBy(a => a.Id).ToList();
        foreach (var d in dates)
        {
            var exact = remaining.FirstOrDefault(a => a.Date == d);
            if (exact is null) continue;
            assigned[d] = (exact.Id, exact.Date, exact.Amount);
            remaining.Remove(exact);
        }
        foreach (var d in dates)
        {
            if (assigned.ContainsKey(d)) continue;
            var near = remaining
                .Where(a => Math.Abs(a.Date.DayNumber - d.DayNumber) <= window)
                .OrderBy(a => Math.Abs(a.Date.DayNumber - d.DayNumber))
                .ThenBy(a => a.Date).ThenBy(a => a.Id)
                .FirstOrDefault();
            if (near is null) continue;
            assigned[d] = (near.Id, near.Date, near.Amount);
            remaining.Remove(near);
        }

        return dates.Select(d =>
        {
            var got = assigned.TryGetValue(d, out var actual);
            return new ProjectedPaycheckDto(
                d,
                src.Amount,
                got,
                got ? actual.Id : null,
                got ? actual.Amount : null,
                got ? actual.Date : null);
        }).ToList();
    }

    public async Task<IncomeForecastDto> GetForecastAsync(Guid itemId, int year, CancellationToken ct = default)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var all = await GetPaychecksAsync(itemId, yearStart, yearEnd, ct);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var months = new List<MonthlyIncomeRowDto>();
        for (var m = 1; m <= 12; m++)
        {
            var monthChecks = all.Where(p => p.Date.Month == m).ToList();
            if (monthChecks.Count == 0) continue;

            var projected = monthChecks.Sum(p => p.ProjectedAmount);
            var actualPays = monthChecks.Where(p => p.IsReceived).ToList();
            decimal? actual = actualPays.Count > 0 ? actualPays.Sum(p => p.ActualAmount ?? 0m) : null;

            months.Add(new MonthlyIncomeRowDto(
                year, m, projected, actual,
                monthChecks.Count,
                monthChecks.Count >= 3));
        }

        var receivedYtd = all.Where(p => p.IsReceived).Sum(p => p.ActualAmount ?? 0m);
        var projectedYtd = all.Where(p => p.Date <= today).Sum(p => p.ProjectedAmount);
        var projectedYearTotal = all.Sum(p => p.ProjectedAmount);
        var threePay = months.Where(m => m.IsThreePaycheckMonth).Select(m => new DateOnly(m.Year, m.Month, 1)).ToList();

        return new IncomeForecastDto(
            year, receivedYtd, projectedYtd, projectedYearTotal,
            all.Count, months, threePay);
    }

    public async Task ConfirmPaycheckAsync(Guid itemId, DateOnly date, decimal actualAmount, CancellationToken ct = default)
    {
        if (actualAmount <= 0) throw new ArgumentException("Amount must be positive.", nameof(actualAmount));

        var src = await _db.Items.FirstAsync(s => s.Id == itemId, ct);

        var existing = await _db.Transactions
            .FirstOrDefaultAsync(t => t.ItemId == itemId && t.Date == date, ct);

        var account = await _db.Accounts.FirstAsync(a => a.Id == src.AccountId, ct);

        // Income credits the account; expense debits it.
        var sign = src.Direction == TransactionDirection.Expense ? -1m : 1m;

        Guid paycheckId;
        if (existing is null)
        {
            var txn = new Transaction
            {
                Date = date,
                AccountId = src.AccountId,
                CategoryId = src.CategoryId,
                Amount = actualAmount,
                Direction = src.Direction,
                ItemId = itemId,
                Note = src.Name,
            };
            _db.Transactions.Add(txn);
            account.CurrentBalance += sign * actualAmount;
            paycheckId = txn.Id;
        }
        else
        {
            var delta = actualAmount - existing.Amount;
            existing.Amount = actualAmount;
            account.CurrentBalance += sign * delta;
            paycheckId = existing.Id;
        }

        // Keep the "move to savings" transfer in sync with this paycheck (create / adjust / remove).
        if (src.Direction == TransactionDirection.Income)
            await SyncSavingsMoveAsync(src, account, paycheckId, actualAmount, date, ct);

        await _db.SaveChangesAsync(ct);
    }

    public async Task<RunwayDto> GetRunwayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var searchHorizon = today.AddDays(370); // far enough to find the next income even if monthly

        // Starting point: current total across active accounts. (Summed client-side so an empty
        // set yields 0 rather than a null-SUM cast error.)
        var balances = await _db.Accounts.AsNoTracking()
            .Where(a => !a.IsArchived)
            .Select(a => a.CurrentBalance)
            .ToListAsync(ct);
        var startingBalance = balances.Sum();

        // Only scheduled items can produce future occurrences; ad-hoc items never project.
        var sources = await _db.Items.AsNoTracking()
            .Where(s => !s.IsArchived && s.Frequency != null)
            .Select(s => new { s.Id, s.Name, s.Direction })
            .ToListAsync(ct);

        // 1) The destination: the soonest unreceived income occurrence across all income rules.
        DateOnly? incomeDate = null;
        string? incomeName = null;
        decimal incomeAmount = 0m;
        foreach (var s in sources.Where(s => s.Direction == TransactionDirection.Income))
        {
            var next = (await GetPaychecksAsync(s.Id, today, searchHorizon, ct))
                .FirstOrDefault(p => p.Date >= today && !p.IsReceived);
            if (next is not null && (incomeDate is null || next.Date < incomeDate))
            {
                incomeDate = next.Date;
                incomeName = s.Name;
                incomeAmount = next.ProjectedAmount;
            }
        }

        var hasIncome = incomeDate is not null;
        var endDate = incomeDate ?? today.AddDays(30); // no income? still show the next 30 days

        // 2) Every unreceived expense occurrence from today through the income date.
        var events = new List<(Guid SourceId, DateOnly Date, string Name, decimal Amount)>();
        foreach (var s in sources.Where(s => s.Direction == TransactionDirection.Expense))
        {
            foreach (var p in await GetPaychecksAsync(s.Id, today, endDate, ct))
                if (p.Date >= today && p.Date <= endDate && !p.IsReceived)
                    events.Add((s.Id, p.Date, s.Name, p.ProjectedAmount));
        }
        events.Sort((a, b) => a.Date != b.Date
            ? a.Date.CompareTo(b.Date)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        // 3) Walk the running balance down through the expenses.
        var running = startingBalance;
        var lowest = startingBalance;
        decimal totalExpenses = 0m;
        var expenses = new List<RunwayEventDto>(events.Count);
        foreach (var e in events)
        {
            running -= e.Amount;
            totalExpenses += e.Amount;
            if (running < lowest) lowest = running;
            expenses.Add(new RunwayEventDto(e.SourceId, e.Date, e.Name, e.Amount, running));
        }

        var days = hasIncome ? incomeDate!.Value.DayNumber - today.DayNumber : 0;

        return new RunwayDto(
            today, startingBalance, hasIncome, incomeDate, incomeName, incomeAmount,
            days, totalExpenses, lowest, lowest < 0m, expenses);
    }

    public async Task<DateOnly?> GetNextOccurrenceAsync(Guid itemId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var horizon = today.AddDays(370); // far enough to catch a monthly rule's next date
        var checks = await GetPaychecksAsync(itemId, today, horizon, ct);
        return checks.FirstOrDefault(p => p.Date >= today && !p.IsReceived)?.Date;
    }

    public async Task MarkPaidTodayAsync(Guid itemId, decimal? amount = null, CancellationToken ct = default)
    {
        var src = await _db.Items.FirstAsync(s => s.Id == itemId, ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var amt = amount ?? src.Amount;
        if (amt <= 0) throw new ArgumentException("Amount must be positive.", nameof(amount));

        // Re-anchor the recurrence so the next occurrence counts from today. Ad-hoc items
        // (no schedule) have nothing to re-anchor — paying one is just a normal log.
        if (src.Frequency is not null) src.StartDate = today;

        // Record (or update) today's payment, linked to the rule, adjusting the balance.
        var account = await _db.Accounts.FirstAsync(a => a.Id == src.AccountId, ct);
        var sign = src.Direction == TransactionDirection.Expense ? -1m : 1m;
        var existing = await _db.Transactions
            .FirstOrDefaultAsync(t => t.ItemId == itemId && t.Date == today, ct);
        Guid paycheckId;
        if (existing is null)
        {
            var txn = new Transaction
            {
                Date = today,
                AccountId = src.AccountId,
                CategoryId = src.CategoryId,
                Amount = amt,
                Direction = src.Direction,
                ItemId = itemId,
                Note = src.Name,
            };
            _db.Transactions.Add(txn);
            account.CurrentBalance += sign * amt;
            paycheckId = txn.Id;
        }
        else
        {
            account.CurrentBalance += sign * (amt - existing.Amount);
            existing.Amount = amt;
            paycheckId = existing.Id;
        }

        // Income "paid today" feeds savings too (mirrors ConfirmPaycheck).
        if (src.Direction == TransactionDirection.Income)
            await SyncSavingsMoveAsync(src, account, paycheckId, amt, today, ct);

        await _db.SaveChangesAsync(ct);
    }

    // Reconcile the "move to savings" transfer for a just-received/edited paycheck: remove any
    // prior move tied to it (reversing balances), then create a fresh one if a move-mode goal is
    // active and the rule saves a %. Idempotent — handles re-confirms, amount edits, and mode
    // changes correctly. (Runs before the caller's SaveChanges.)
    private async Task SyncSavingsMoveAsync(Item src, Account srcAccount, Guid paycheckTxnId, decimal received, DateOnly date, CancellationToken ct)
    {
        var prior = await _db.Transactions.Where(t => t.SavingsSourceTransactionId == paycheckTxnId).ToListAsync(ct);
        foreach (var leg in prior)
        {
            var acct = leg.AccountId == srcAccount.Id ? srcAccount : await _db.Accounts.FirstAsync(a => a.Id == leg.AccountId, ct);
            acct.CurrentBalance -= leg.Direction == TransactionDirection.Income ? leg.Amount : -leg.Amount;
            _db.Transactions.Remove(leg);
        }

        if (src.SavingsPercent <= 0) return;
        var goal = await _db.SavingsGoals
            .Where(g => !g.IsArchived && g.Mode == SavingsMode.MoveToSavings && g.LinkedAccountId != null)
            .OrderBy(g => g.CreatedUtc)
            .FirstOrDefaultAsync(ct);
        if (goal?.LinkedAccountId is not Guid linkedId || linkedId == src.AccountId) return;

        var saveAmt = Math.Round(received * src.SavingsPercent / 100m, 2);
        if (saveAmt <= 0m) return;

        var linked = await _db.Accounts.FirstAsync(a => a.Id == linkedId, ct);
        var groupId = Guid.NewGuid();
        var note = "Savings → " + goal.Name;
        _db.Transactions.Add(new Transaction { Date = date, AccountId = srcAccount.Id, Amount = saveAmt, Direction = TransactionDirection.Expense, Note = note, TransferGroupId = groupId, SavingsSourceTransactionId = paycheckTxnId, SavingsGoalId = goal.Id });
        _db.Transactions.Add(new Transaction { Date = date, AccountId = linkedId, Amount = saveAmt, Direction = TransactionDirection.Income, Note = note, TransferGroupId = groupId, SavingsSourceTransactionId = paycheckTxnId, SavingsGoalId = goal.Id });
        srcAccount.CurrentBalance -= saveAmt;
        linked.CurrentBalance += saveAmt;
    }

}
