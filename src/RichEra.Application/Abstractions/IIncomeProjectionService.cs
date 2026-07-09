using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface IIncomeProjectionService
{
    /// <summary>Project paychecks for an income source within an inclusive date range.</summary>
    Task<IReadOnlyList<ProjectedPaycheckDto>> GetPaychecksAsync(
        Guid itemId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Year-long forecast: projected vs actual per month + YTD totals + three-paycheck-month flags.</summary>
    Task<IncomeForecastDto> GetForecastAsync(
        Guid itemId, int year, CancellationToken ct = default);

    /// <summary>Confirm a projected paycheck as a real Transaction with the given amount.</summary>
    Task ConfirmPaycheckAsync(Guid itemId, DateOnly date, decimal actualAmount, CancellationToken ct = default);

    /// <summary>Roadmap from today to the next (unreceived) income: every projected expense due
    /// before then, ordered by date, with a running balance from the current total so the UI can
    /// flag running short before payday.</summary>
    Task<RunwayDto> GetRunwayAsync(CancellationToken ct = default);

    /// <summary>The soonest still-unpaid occurrence date for a recurring rule on or after today,
    /// or null if none is scheduled. Same forward-looking logic the runway uses, so the date
    /// matches the "Today's Deals" rail.</summary>
    Task<DateOnly?> GetNextOccurrenceAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Mark a recurring rule paid as of TODAY: records today's payment (linked to the
    /// rule, adjusting the balance) and re-anchors the rule's start date to today, so the next
    /// occurrence counts from now. Amount defaults to the rule's amount.</summary>
    Task MarkPaidTodayAsync(Guid itemId, decimal? amount = null, CancellationToken ct = default);
}
