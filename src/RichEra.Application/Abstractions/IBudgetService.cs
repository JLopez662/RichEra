using RichEra.Application.Dtos;

namespace RichEra.Application.Abstractions;

public interface IBudgetService
{
    /// <summary>The month's budget ring: planned income, savings target, spend budget
    /// (computed or overridden), actual flows, remaining/safe-per-day, and the macro split.</summary>
    Task<BudgetDto> GetBudgetAsync(int year, int month, CancellationToken ct = default);

    /// <summary>Pin (or clear, with null) a manual spend budget for one month, overriding the
    /// computed income − savings default.</summary>
    Task SetMonthlyOverrideAsync(int year, int month, decimal? spendBudget, CancellationToken ct = default);
}
