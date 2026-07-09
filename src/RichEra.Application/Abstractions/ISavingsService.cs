using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

// A single savings goal fed by a per-income savings percentage. "Track only" keeps a running
// tally; "Move to savings" also transfers the saved % into a linked account when paychecks arrive.
public interface ISavingsService
{
    Task<SavingsGoalDto?> GetGoalAsync(CancellationToken ct = default);
    Task<SavingsGoalDto> UpsertGoalAsync(SavingsGoalUpsertDto dto, CancellationToken ct = default);
    Task ClearGoalAsync(CancellationToken ct = default);

    // Set what percent (0–100) of an income's paycheck is set aside for savings.
    Task SetSavingsPercentAsync(Guid itemId, int percent, CancellationToken ct = default);

    // Post a one-off deposit toward the goal (e.g. from the "pay shop"). MoveToSavings transfers
    // `amount` from fromAccountId into the linked savings account (counted via a stamped Income leg);
    // TrackOnly records a balance-neutral contribution that bumps the tally (no money moves;
    // fromAccountId is ignored). Throws if the goal is gone, amount <= 0, or a move-mode deposit is
    // missing/uses an invalid funding account.
    Task PostDepositAsync(Guid goalId, decimal amount, DateOnly date, Guid? fromAccountId = null, CancellationToken ct = default);

    // Goal + derived "saved so far" + projected completion + the incomes feeding it.
    Task<SavingsPlanDto> GetPlanAsync(CancellationToken ct = default);
}
