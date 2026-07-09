namespace RichEra.Domain.Entities;

// A reusable "money item" — the food-database entry of the Money Log concept. Anything paid
// or received repeatedly enough to name: a bill, a subscription, a paycheck, a debt payment,
// a fund buy. Generalizes the old IncomeSource: the recurrence schedule is now OPTIONAL
// (null Frequency = an unscheduled item logged ad hoc), and the item carries the nullable
// "money micros" that power its Money Facts label.
public class Item
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    // Who the money goes to / comes from — the "brand" line ("Netflix", landlord, employer).
    public string? Counterparty { get; set; }

    // Always stored positive: $ per serving. Sign comes from Direction.
    public decimal Amount { get; set; }

    // What one serving is called: "month", "payment", "visit", "tank". Null → unitless "time".
    public string? UnitName { get; set; }

    // Whether logging a serving credits (Income) or debits (Expense) the account.
    public TransactionDirection Direction { get; set; } = TransactionDirection.Income;

    // Macro bucket for the Needs/Wants/Debt/Future split. Null = unassigned (income items too).
    public MacroBucket? Bucket { get; set; }

    // Optional schedule. Both null for ad-hoc items; both set for recurring rules.
    public RepeatFrequency? Frequency { get; set; }
    public DateOnly? StartDate { get; set; }
    public bool HasSchedule => Frequency is not null && StartDate is not null;

    public Guid AccountId { get; set; }   // default account the item pays from / into
    public Account? Account { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Icon { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // Percent (0–100) of each received paycheck to set aside for savings. Income rules only.
    public int SavingsPercent { get; set; }

    // ---- Money micros (all optional) — the Money Facts label reads straight off these. ----
    public decimal? AprPct { get; set; }             // debt interest rate, e.g. 24.99
    public InterestModel Interest { get; set; }      // how AprPct accrues; None when n/a
    public decimal? PrincipalBalance { get; set; }   // remaining balance this item pays down
    public int? PlanPaymentsTotal { get; set; }      // payment-plan length  ("of 48")
    public int? PlanPaymentsMade { get; set; }       // payments already made ("16 of")
    public decimal? ExpectedReturnPct { get; set; }  // expected annual growth, e.g. 7.0
    public decimal? FeesPerUnit { get; set; }        // flat fee baked into each serving
    public bool IsTaxDeductible { get; set; }
}

public enum RepeatFrequency
{
    Weekly = 0,
    Biweekly = 1,
    Monthly = 2,
}

// The money "macros": every outflow rolls up into one of these for the budget split.
public enum MacroBucket
{
    Needs = 0,
    Wants = 1,
    Debt = 2,
    Future = 3,
}

public enum InterestModel
{
    None = 0,
    Simple = 1,
    CompoundDaily = 2,
    CompoundMonthly = 3,
    CompoundAnnually = 4,
}
