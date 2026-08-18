using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;

namespace RichEra.Infrastructure.Services;

// Turns current app state into the secondary notifications that are due right now. Platform-free
// and stateless: it queries the domain services and returns NotificationInputs with STABLE dedupe
// keys, so the Center records each exactly once no matter how often this runs (startup / resume /
// after a save / boot). The MAUI scheduler filters by the user's per-kind toggles and pushes new ones.
public class NotificationRuleEvaluator : INotificationRuleEvaluator
{
    private readonly IItemService _items;
    private readonly IIncomeProjectionService _projection;
    private readonly IBudgetService _budget;
    private readonly ISavingsService _savings;

    public NotificationRuleEvaluator(
        IItemService items, IIncomeProjectionService projection,
        IBudgetService budget, ISavingsService savings)
    {
        _items = items;
        _projection = projection;
        _budget = budget;
        _savings = savings;
    }

    // Checked high → low; the first one the goal has reached is the milestone we celebrate.
    private static readonly int[] Milestones = { 100, 75, 50, 25 };

    public async Task<IReadOnlyList<NotificationInput>> EvaluateAsync(NotificationRuleContext ctx, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var results = new List<NotificationInput>();

        if (ctx.BillsDue) await AddBillsDueAsync(ctx, now, results, ct);
        if (ctx.BudgetAlerts) await AddBudgetAlertAsync(ctx, now, results, ct);
        if (ctx.SavingsMilestones) await AddSavingsMilestoneAsync(now, results, ct);
        if (ctx.WeighIn) AddWeighInNudge(ctx, now, results);

        return results;
    }

    // A recurring expense whose next unpaid occurrence falls within the lead window.
    private async Task AddBillsDueAsync(NotificationRuleContext ctx, DateTime now, List<NotificationInput> results, CancellationToken ct)
    {
        var items = await _items.ListAsync(direction: TransactionDirection.Expense, scheduledOnly: true, ct: ct);
        foreach (var item in items)
        {
            var due = await _projection.GetNextOccurrenceAsync(item.Id, ct);
            if (due is not DateOnly d) continue;

            var days = d.DayNumber - ctx.Today.DayNumber;
            if (days < 0 || days > ctx.BillsLeadDays) continue;

            var when = days switch { 0 => "today", 1 => "tomorrow", _ => $"in {days} days" };
            results.Add(new NotificationInput(
                NotificationKind.BillDue,
                $"{item.Name} due {when}",
                $"{item.Amount:C0} on {d:MMM d}.",
                now,
                DedupeKey: $"bills|{item.Id}|{d:yyyyMMdd}"));   // once per occurrence
        }
    }

    // Crossing the spend budget: the threshold once, then over-budget once, per month.
    private async Task AddBudgetAlertAsync(NotificationRuleContext ctx, DateTime now, List<NotificationInput> results, CancellationToken ct)
    {
        var b = await _budget.GetBudgetAsync(ctx.Today.Year, ctx.Today.Month, ct);
        if (b.SpendBudget <= 0m) return;

        var pct = (int)Math.Floor(b.Spent / b.SpendBudget * 100m);
        var monthKey = $"{ctx.Today.Year:D4}-{ctx.Today.Month:D2}";
        var monthName = new DateOnly(ctx.Today.Year, ctx.Today.Month, 1).ToString("MMMM");

        if (pct >= 100)
        {
            results.Add(new NotificationInput(
                NotificationKind.BudgetAlert,
                $"You're over {monthName}'s budget",
                $"{b.Spent:C0} of {b.SpendBudget:C0} — {(b.Spent - b.SpendBudget):C0} over.",
                now,
                DedupeKey: $"budget|{monthKey}|over"));
        }
        else if (pct >= ctx.BudgetThresholdPct)
        {
            results.Add(new NotificationInput(
                NotificationKind.BudgetAlert,
                $"You've used {pct}% of {monthName}'s budget",
                $"{b.Spent:C0} of {b.SpendBudget:C0} · {b.Remaining:C0} left.",
                now,
                DedupeKey: $"budget|{monthKey}|{ctx.BudgetThresholdPct}"));
        }
    }

    // The highest 25/50/75/100 milestone the primary goal has reached, once each.
    private async Task AddSavingsMilestoneAsync(DateTime now, List<NotificationInput> results, CancellationToken ct)
    {
        var plan = await _savings.GetPlanAsync(ct);
        if (!plan.HasGoal || plan.Goal is null) return;

        var reached = Milestones.FirstOrDefault(m => plan.PercentComplete >= m);
        if (reached == 0) return;

        var g = plan.Goal;
        results.Add(new NotificationInput(
            NotificationKind.SavingsMilestone,
            reached >= 100 ? $"{g.Name}: goal reached! 🎉" : $"{g.Name}: {reached}% there",
            $"{plan.SavedSoFar:C0} of {g.TargetAmount:C0} saved.",
            now,
            DedupeKey: $"savings|{g.Id}|{reached}"));
    }

    // A gentle weekly prompt to review balances — once per ISO week.
    private static void AddWeighInNudge(NotificationRuleContext ctx, DateTime now, List<NotificationInput> results)
    {
        var anchor = ctx.Today.ToDateTime(TimeOnly.MinValue);
        var week = System.Globalization.ISOWeek.GetWeekOfYear(anchor);
        var year = System.Globalization.ISOWeek.GetYear(anchor);
        results.Add(new NotificationInput(
            NotificationKind.WeighInReminder,
            "Weekly money check-in",
            "Take a moment to review your accounts and net worth.",
            now,
            DedupeKey: $"weigh|{year:D4}-W{week:D2}"));
    }
}
