using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

// The month's budget ring — Lose It's calorie budget, for dollars.
//
//   SpendBudget = override ?? (PlannedIncome − SavingsTarget)      pay-yourself-first
//   ExtraIn     = max(0, MoneyIn − PlannedIncome)                  income beyond plan earns
//   Remaining   = SpendBudget + ExtraIn − Spent                    budget back ("exercise")
//
// Planned income comes from the same projection engine the paycheck pages use, so the ring
// and the ledger can never disagree about what this month should bring in.
public class BudgetService : IBudgetService
{
    private const string OverrideKeyPrefix = "budget.override.";

    private readonly AppDbContext _db;
    private readonly IIncomeProjectionService _projection;

    public BudgetService(AppDbContext db, IIncomeProjectionService projection)
    {
        _db = db;
        _projection = projection;
    }

    public async Task<BudgetDto> GetBudgetAsync(int year, int month, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1); // inclusive, for projections
        var monthNext = monthStart.AddMonths(1);            // exclusive, for row queries

        // ---- Planned income + savings target, from scheduled income items ----
        var incomeItems = await _db.Items.AsNoTracking()
            .Where(s => !s.IsArchived && s.Direction == TransactionDirection.Income && s.Frequency != null)
            .Select(s => new { s.Id, s.SavingsPercent })
            .ToListAsync(ct);

        decimal plannedIncome = 0m, savingsTarget = 0m;
        foreach (var src in incomeItems)
        {
            var occurrences = await _projection.GetPaychecksAsync(src.Id, monthStart, monthEnd, ct);
            foreach (var occ in occurrences)
            {
                var amount = occ.ActualAmount ?? occ.ProjectedAmount;
                plannedIncome += amount;
                savingsTarget += Math.Round(amount * src.SavingsPercent / 100m, 2);
            }
        }

        // ---- Actual flows: transfers and reconcile adjustments are movement, not money ----
        var rows = await _db.Transactions.AsNoTracking()
            .Where(t => t.TransferGroupId == null && t.ImportSource != "reconcile"
                        && t.Date >= monthStart && t.Date < monthNext)
            .Select(t => new
            {
                t.Amount,
                t.Direction,
                ItemBucket = t.Item != null ? t.Item.Bucket : null,
                CategoryBucket = t.Category != null ? t.Category.Bucket : null,
            })
            .ToListAsync(ct);

        var spent = rows.Where(r => r.Direction == TransactionDirection.Expense).Sum(r => r.Amount);
        var moneyIn = rows.Where(r => r.Direction == TransactionDirection.Income).Sum(r => r.Amount);

        // ---- Macro split of the outflow: item bucket wins, category bucket fills in ----
        var macros = rows
            .Where(r => r.Direction == TransactionDirection.Expense)
            .GroupBy(r => r.ItemBucket ?? r.CategoryBucket)
            .Select(g => (Bucket: g.Key, Amount: g.Sum(r => r.Amount)))
            .Where(s => s.Amount > 0m)
            .OrderBy(s => s.Bucket is null)      // Unsorted last
            .ThenBy(s => s.Bucket)
            .Select(s => new BucketSliceDto(
                s.Bucket, s.Amount,
                (int)Math.Round(Math.Clamp(s.Amount / Math.Max(spent, 0.01m) * 100m, 0m, 100m))))
            .ToList();

        // ---- Budget: manual pin beats the computed default ----
        var overrideBudget = await GetOverrideAsync(year, month, ct);
        var spendBudget = overrideBudget ?? Math.Max(0m, plannedIncome - savingsTarget);

        var extraIn = Math.Max(0m, moneyIn - plannedIncome);
        var effectiveBudget = spendBudget + extraIn;
        var remaining = effectiveBudget - spent;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var daysLeft =
            monthStart > today ? daysInMonth :                       // future month: untouched
            monthNext <= today ? 0 :                                 // past month: closed
            daysInMonth - today.Day + 1;                             // current: today counts
        var safePerDay = daysLeft > 0 ? Math.Round(Math.Max(0m, remaining) / daysLeft, 2) : 0m;

        return new BudgetDto(
            year, month,
            Math.Round(plannedIncome, 2), Math.Round(savingsTarget, 2),
            Math.Round(spendBudget, 2), overrideBudget is not null,
            Math.Round(spent, 2), Math.Round(moneyIn, 2), Math.Round(extraIn, 2),
            Math.Round(effectiveBudget, 2), Math.Round(remaining, 2),
            safePerDay, daysLeft, macros);
    }

    public async Task SetMonthlyOverrideAsync(int year, int month, decimal? spendBudget, CancellationToken ct = default)
    {
        if (spendBudget is < 0m)
            throw new ArgumentException("A budget can't be negative.", nameof(spendBudget));

        var key = OverrideKey(year, month);
        var existing = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (spendBudget is not decimal value)
        {
            if (existing is null) return;
            _db.Settings.Remove(existing);
        }
        else if (existing is null)
        {
            _db.Settings.Add(new Setting { Key = key, Value = value.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            existing.Value = value.ToString(CultureInfo.InvariantCulture);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<decimal?> GetOverrideAsync(int year, int month, CancellationToken ct)
    {
        var key = OverrideKey(year, month);
        var raw = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static string OverrideKey(int year, int month) =>
        $"{OverrideKeyPrefix}{year:D4}-{month:D2}";
}
