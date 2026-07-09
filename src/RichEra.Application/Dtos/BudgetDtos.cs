using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

// One slice of the month's outflow macro bar. Null bucket = not yet assigned
// (no item bucket and no category bucket) — rendered as "Unsorted".
public record BucketSliceDto(MacroBucket? Bucket, decimal Amount, int Pct);

// The month's budget ring, Lose It-style:
//   SpendBudget     = override ?? (PlannedIncome − SavingsTarget)   — pay-yourself-first
//   ExtraIn         = money in beyond plan → earns budget back (the "exercise" credit)
//   Remaining       = SpendBudget + ExtraIn − Spent                 (negative = over)
public record BudgetDto(
    int Year,
    int Month,
    decimal PlannedIncome,     // projected income occurrences this month (actuals where received)
    decimal SavingsTarget,     // Σ income items: occurrence amounts × SavingsPercent
    decimal SpendBudget,
    bool IsManualBudget,       // true when a per-month override is pinned in Settings
    decimal Spent,             // real outflow (transfers & reconcile adjustments excluded)
    decimal MoneyIn,           // real inflow  (same exclusions)
    decimal ExtraIn,
    decimal EffectiveBudget,   // SpendBudget + ExtraIn
    decimal Remaining,
    decimal SafePerDay,        // Remaining spread over DaysLeft; 0 when over or month closed
    int DaysLeft,              // days left including today; 0 for past months
    IReadOnlyList<BucketSliceDto> Macros);
