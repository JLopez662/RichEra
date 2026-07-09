using RichEra.Domain.Entities;

namespace RichEra.Application.Dtos;

public record ItemDto(
    Guid Id,
    string Name,
    decimal Amount,                      // $ per serving
    TransactionDirection Direction,
    RepeatFrequency? Frequency,          // null = unscheduled (ad-hoc) item
    DateOnly? StartDate,
    Guid AccountId,
    string AccountName,
    Guid? CategoryId,
    string? CategoryName,
    string? Icon,
    bool IsArchived,
    int SavingsPercent = 0,              // 0–100 of each paycheck auto-saved; income only
    string? Counterparty = null,
    string? UnitName = null,             // "month", "visit", "payment"…
    MacroBucket? Bucket = null,
    decimal? AprPct = null,
    InterestModel Interest = InterestModel.None,
    decimal? PrincipalBalance = null,
    int? PlanPaymentsTotal = null,
    int? PlanPaymentsMade = null,
    decimal? ExpectedReturnPct = null,
    decimal? FeesPerUnit = null,
    bool IsTaxDeductible = false)
{
    public bool HasSchedule => Frequency is not null && StartDate is not null;
}

public record ItemUpsertDto(
    Guid? Id,
    string Name,
    decimal Amount,
    TransactionDirection Direction,
    RepeatFrequency? Frequency,
    DateOnly? StartDate,
    Guid AccountId,
    Guid? CategoryId,
    string? Icon,
    int SavingsPercent = 0,
    string? Counterparty = null,
    string? UnitName = null,
    MacroBucket? Bucket = null,
    decimal? AprPct = null,
    InterestModel Interest = InterestModel.None,
    decimal? PrincipalBalance = null,
    int? PlanPaymentsTotal = null,
    int? PlanPaymentsMade = null,
    decimal? ExpectedReturnPct = null,
    decimal? FeesPerUnit = null,
    bool IsTaxDeductible = false);
