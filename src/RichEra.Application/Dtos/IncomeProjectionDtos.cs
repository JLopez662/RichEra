namespace RichEra.Application.Dtos;

/// <summary>
/// A projected or actual occurrence of a scheduled Item within a date window.
/// IsReceived = true when ActualTransactionId is non-null.
/// </summary>
public record ProjectedPaycheckDto(
    DateOnly Date,
    decimal ProjectedAmount,
    bool IsReceived,
    Guid? ActualTransactionId,
    decimal? ActualAmount,
    DateOnly? ActualDate = null); // when the payment actually landed — may differ from Date
                                  // by a few days (bank posting lag)

public record MonthlyIncomeRowDto(
    int Year,
    int Month,
    decimal Projected,
    decimal? Actual,
    int PaycheckCount,
    bool IsThreePaycheckMonth);

public record IncomeForecastDto(
    int Year,
    decimal ReceivedYtd,
    decimal ProjectedYtd,
    decimal ProjectedYearTotal,
    int PaychecksProjected,
    IReadOnlyList<MonthlyIncomeRowDto> Months,
    IReadOnlyList<DateOnly> ThreePaycheckMonths);
