namespace RichEra.Application.Dtos;

// A projected expense between now and the next income, with the running balance left after it.
public record RunwayEventDto(
    Guid SourceId,           // the recurring rule, so the UI can mark it paid
    DateOnly Date,
    string Name,
    decimal Amount,          // expense magnitude (positive)
    decimal BalanceAfter);   // running balance once this expense is paid

// "Roadmap to payday": the dated expenses from today up to the next (unreceived) income,
// plus a running balance from the current total so the UI can flag going short.
public record RunwayDto(
    DateOnly Today,
    decimal StartingBalance,        // current total across non-archived accounts
    bool HasIncome,                 // is there an upcoming income to aim for?
    DateOnly? NextIncomeDate,
    string? NextIncomeName,
    decimal NextIncomeAmount,
    int DaysToIncome,
    decimal TotalExpenses,          // sum of the expenses before income
    decimal LowestBalance,          // minimum running balance reached before income
    bool GoesNegative,              // LowestBalance < 0 (you'd run short before payday)
    IReadOnlyList<RunwayEventDto> Expenses);
