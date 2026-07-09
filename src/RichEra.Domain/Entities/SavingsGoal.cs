namespace RichEra.Domain.Entities;

public enum SavingsMode
{
    // Count the saved % as a running tally — money stays in your existing accounts.
    TrackOnly = 0,
    // Transfer the saved % into a linked savings account when a paycheck is received.
    MoveToSavings = 1,
}

public class SavingsGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public SavingsMode Mode { get; set; } = SavingsMode.TrackOnly;

    // Savings account that MoveToSavings transfers into. Null for TrackOnly.
    public Guid? LinkedAccountId { get; set; }
    public Account? LinkedAccount { get; set; }

    // Contributions are counted from this date forward (defaults to when the goal was created).
    public DateOnly StartDate { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsArchived { get; set; }
}
