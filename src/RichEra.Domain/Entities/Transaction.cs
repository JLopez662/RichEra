namespace RichEra.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    // Always stored positive. Sign comes from Direction.
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }

    // User-given name for the row ("Weekly groceries", "Netflix"). The primary display label
    // for manual transactions. Null for older/imported rows, which fall back to item/category.
    public string? Name { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    // Both legs of a transfer share this id. Null for normal transactions.
    public Guid? TransferGroupId { get; set; }

    // Set when this transaction logs an Item — a confirmed occurrence of its schedule, or an
    // ad-hoc log from the library. Null for free-form manual rows.
    public Guid? ItemId { get; set; }
    public Item? Item { get; set; }

    // How many servings of the item this row represents (2 × "movie ticket"). 1 for plain rows;
    // Amount is always the TOTAL for the row, not per-serving.
    public decimal Quantity { get; set; } = 1m;

    // Set when imported from a bank statement file (e.g. OFX FITID). Together with
    // AccountId this is the dedupe key, so re-importing overlapping ranges is idempotent.
    public string? ExternalId { get; set; }
    // Which import produced this row (e.g. "ofx"). Null for manual/recurrence transactions.
    public string? ImportSource { get; set; }

    // For a "move to savings" transfer leg: the paycheck transaction that spawned it, so deleting
    // or re-confirming that paycheck can unwind/adjust the moved amount. Null for everything else.
    public Guid? SavingsSourceTransactionId { get; set; }

    // For a "move to savings" transfer leg: the goal it counts toward, so progress is scoped to a
    // specific goal (unambiguous across archive/recreate and reused accounts). Null otherwise.
    public Guid? SavingsGoalId { get; set; }
}

public enum TransactionDirection
{
    Expense = 0,
    Income = 1,
}
