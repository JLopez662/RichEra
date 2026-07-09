namespace RichEra.Application.Dtos;

public record MonthlySummaryDto(
    int Year,
    int Month,
    decimal Income,
    decimal Expenses,
    decimal Net,
    decimal TotalAccountBalance);
