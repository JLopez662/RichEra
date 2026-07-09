using Microsoft.EntityFrameworkCore;
using RichEra.Application.Abstractions;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;

namespace RichEra.Infrastructure.Services;

public class CashFlowService : ICashFlowService
{
    private readonly AppDbContext _db;
    public CashFlowService(AppDbContext db) => _db = db;

    public async Task<MonthlySummaryDto> GetMonthlySummaryAsync(int year, int month, CancellationToken ct = default)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);

        // Exclude transfers and balance-reconciliation adjustments from income/expense totals —
        // neither is money actually earned or spent, just movement/correction.
        var nonTransfers = _db.Transactions.AsNoTracking()
            .Where(t => t.TransferGroupId == null && t.ImportSource != "reconcile"
                        && t.Date >= from && t.Date < to);

        var income = await nonTransfers
            .Where(t => t.Direction == TransactionDirection.Income)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var expenses = await nonTransfers
            .Where(t => t.Direction == TransactionDirection.Expense)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var totalBalance = await _db.Accounts.AsNoTracking()
            .Where(a => !a.IsArchived)
            .SumAsync(a => (decimal?)a.CurrentBalance, ct) ?? 0m;

        return new MonthlySummaryDto(year, month, income, expenses, income - expenses, totalBalance);
    }
}
