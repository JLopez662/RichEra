using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public record SavingsGoalDto(
    Guid Id,
    string Name,
    decimal TargetAmount,
    SavingsMode Mode,
    Guid? LinkedAccountId,
    string? LinkedAccountName,
    DateOnly StartDate);

public record SavingsGoalUpsertDto(
    Guid? Id,
    string Name,
    decimal TargetAmount,
    SavingsMode Mode,
    Guid? LinkedAccountId);

// One income feeding the goal, with its per-paycheck savings contribution.
public record SavingsContributorDto(
    Guid ItemId,
    string Name,
    int SavingsPercent,
    decimal PerPaycheck,
    RepeatFrequency Frequency);

public record SavingsPlanDto(
    bool HasGoal,
    SavingsGoalDto? Goal,
    decimal SavedSoFar,
    decimal Remaining,
    int PercentComplete,
    decimal PerMonthContribution,
    DateOnly? ProjectedCompletion,
    IReadOnlyList<SavingsContributorDto> Contributors);
