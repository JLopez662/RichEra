using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// DB-backed tests for IncomeProjectionService.GetRunwayAsync against in-memory SQLite.
public class RunwayTests
{
    private sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _conn;
        public AppDbContext Db { get; }

        public TestDb()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open();
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
            Db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Db.Dispose();
            _conn.Dispose();
        }
    }

    [Fact]
    public async Task Runway_FindsNextIncome_ListsExpensesBefore_AndFlagsShort()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var acct = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 100m };
        t.Db.Accounts.Add(acct);
        t.Db.Items.AddRange(
            new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today.AddDays(10), AccountId = acct.Id },
            new Item { Name = "Rent", Amount = 150m, Direction = TransactionDirection.Expense, Frequency = RepeatFrequency.Monthly, StartDate = today.AddDays(3), AccountId = acct.Id }
        );
        await t.Db.SaveChangesAsync();

        var rw = await new IncomeProjectionService(t.Db).GetRunwayAsync();

        Assert.True(rw.HasIncome);
        Assert.Equal(today.AddDays(10), rw.NextIncomeDate);
        Assert.Equal("Payroll", rw.NextIncomeName);
        Assert.Equal(10, rw.DaysToIncome);

        var e = Assert.Single(rw.Expenses);          // only the Rent occurrence falls before payday
        Assert.Equal("Rent", e.Name);
        Assert.Equal(today.AddDays(3), e.Date);
        Assert.Equal(150m, e.Amount);
        Assert.Equal(-50m, e.BalanceAfter);          // 100 - 150

        Assert.Equal(150m, rw.TotalExpenses);
        Assert.Equal(-50m, rw.LowestBalance);
        Assert.True(rw.GoesNegative);
    }

    [Fact]
    public async Task Runway_NoIncomeRule_ReportsNoIncome()
    {
        using var t = new TestDb();
        t.Db.Accounts.Add(new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 100m });
        await t.Db.SaveChangesAsync();

        var rw = await new IncomeProjectionService(t.Db).GetRunwayAsync();

        Assert.False(rw.HasIncome);
        Assert.Null(rw.NextIncomeDate);
        Assert.Equal(100m, rw.StartingBalance);
    }

    [Fact]
    public async Task MarkPaidToday_ReAnchorsScheduleAndRecordsPayment()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var acct = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 500m };
        t.Db.Accounts.Add(acct);
        var rule = new Item { Name = "Gym", Amount = 30m, Direction = TransactionDirection.Expense, Frequency = RepeatFrequency.Monthly, StartDate = today.AddDays(-3), AccountId = acct.Id };
        t.Db.Items.Add(rule);
        await t.Db.SaveChangesAsync();

        await new IncomeProjectionService(t.Db).MarkPaidTodayAsync(rule.Id);

        var updatedRule = await t.Db.Items.AsNoTracking().FirstAsync(s => s.Id == rule.Id);
        Assert.Equal(today, updatedRule.StartDate); // re-anchored to today

        var txn = await t.Db.Transactions.AsNoTracking().FirstAsync(x => x.ItemId == rule.Id);
        Assert.Equal(today, txn.Date);
        Assert.Equal(30m, txn.Amount);
        Assert.Equal(TransactionDirection.Expense, txn.Direction);

        var acctAfter = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == acct.Id);
        Assert.Equal(470m, acctAfter.CurrentBalance); // 500 − 30 (expense debits)
    }
}
