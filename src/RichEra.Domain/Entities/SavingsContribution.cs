namespace RichEra.Domain.Entities;

// A manual, balance-neutral contribution toward a TrackOnly savings goal — money the user set
// aside themselves, recorded so the goal's tally reflects it. (MoveToSavings goals don't use this:
// they record real two-leg transfers stamped with the goal id instead — see
// IncomeProjectionService.SyncSavingsMoveAsync and SavingsService.PostDepositAsync.)
public class SavingsContribution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoalId { get; set; }
    public SavingsGoal? Goal { get; set; }

    public decimal Amount { get; set; }   // always positive; no account balance is affected
    public DateOnly Date { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
