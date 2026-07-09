using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// DB-backed tests for BudgetService against in-memory SQLite. All scenarios use May 2026 —
// a month guaranteed to be in the past — so DaysLeft/SafePerDay are deterministically 0 and
// the assertions focus on the money math.
public class BudgetServiceTests
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

    private static BudgetService NewService(AppDbContext db) =>
        new(db, new IncomeProjectionService(db));

    // One scheduled paycheck (10% auto-saved), real spending with buckets on both the item
    // and the category level, plus flows that must NOT count (transfer legs, reconcile rows),
    // and side income beyond the plan that earns budget back.
    [Fact]
    public async Task Budget_ComputesPlan_Flows_ExtraIn_AndMacroSplit()
    {
        using var t = new TestDb();
        var acct = new Account { Name = "Checking", Type = AccountType.Checking };
        t.Db.Accounts.Add(acct);

        var payroll = new Item
        {
            Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income,
            Frequency = RepeatFrequency.Monthly, StartDate = new DateOnly(2026, 5, 2),
            AccountId = acct.Id, SavingsPercent = 10,
        };
        var needsCat = new Category { Name = "Housing", Kind = CategoryKind.Expense, Bucket = MacroBucket.Needs };
        // The coffee item's own bucket (Wants) must beat its category's bucket (Needs).
        var coffee = new Item
        {
            Name = "Coffee cart", Amount = 25m, Direction = TransactionDirection.Expense,
            AccountId = acct.Id, Bucket = MacroBucket.Wants, CategoryId = needsCat.Id,
        };
        t.Db.Categories.Add(needsCat);
        t.Db.Items.AddRange(payroll, coffee);

        t.Db.Transactions.AddRange(
            // Received paycheck — its actual amount IS the plan, not extra.
            new Transaction { Date = new DateOnly(2026, 5, 2), AccountId = acct.Id, Amount = 1000m, Direction = TransactionDirection.Income, ItemId = payroll.Id },
            // Side income beyond the plan earns budget back.
            new Transaction { Date = new DateOnly(2026, 5, 10), AccountId = acct.Id, Amount = 120m, Direction = TransactionDirection.Income },
            // Spending: category-bucketed rent + item-bucketed coffee (2 servings).
            new Transaction { Date = new DateOnly(2026, 5, 3), AccountId = acct.Id, Amount = 300m, Direction = TransactionDirection.Expense, CategoryId = needsCat.Id },
            new Transaction { Date = new DateOnly(2026, 5, 5), AccountId = acct.Id, Amount = 50m, Direction = TransactionDirection.Expense, ItemId = coffee.Id, Quantity = 2m },
            // Transfer legs and reconcile adjustments are movement, not money.
            new Transaction { Date = new DateOnly(2026, 5, 6), AccountId = acct.Id, Amount = 200m, Direction = TransactionDirection.Expense, TransferGroupId = Guid.NewGuid() },
            new Transaction { Date = new DateOnly(2026, 5, 7), AccountId = acct.Id, Amount = 77m, Direction = TransactionDirection.Income, ImportSource = "reconcile" });
        await t.Db.SaveChangesAsync();

        var b = await NewService(t.Db).GetBudgetAsync(2026, 5);

        Assert.Equal(1000m, b.PlannedIncome);
        Assert.Equal(100m, b.SavingsTarget);
        Assert.Equal(900m, b.SpendBudget);          // planned − savings target
        Assert.False(b.IsManualBudget);
        Assert.Equal(350m, b.Spent);                // 300 + 50; transfer leg excluded
        Assert.Equal(1120m, b.MoneyIn);             // 1000 + 120; reconcile row excluded
        Assert.Equal(120m, b.ExtraIn);              // beyond the 1000 plan
        Assert.Equal(1020m, b.EffectiveBudget);
        Assert.Equal(670m, b.Remaining);
        Assert.Equal(0, b.DaysLeft);                // May 2026 is closed
        Assert.Equal(0m, b.SafePerDay);

        Assert.Equal(2, b.Macros.Count);
        var needs = Assert.Single(b.Macros, m => m.Bucket == MacroBucket.Needs);
        Assert.Equal(300m, needs.Amount);
        Assert.Equal(86, needs.Pct);
        var wants = Assert.Single(b.Macros, m => m.Bucket == MacroBucket.Wants);
        Assert.Equal(50m, wants.Amount);            // item bucket beat the category bucket
        Assert.Equal(14, wants.Pct);
    }

    [Fact]
    public async Task Budget_ManualOverride_Wins_AndClears()
    {
        using var t = new TestDb();
        var acct = new Account { Name = "Checking", Type = AccountType.Checking };
        t.Db.Accounts.Add(acct);
        t.Db.Items.Add(new Item
        {
            Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income,
            Frequency = RepeatFrequency.Monthly, StartDate = new DateOnly(2026, 5, 2),
            AccountId = acct.Id, SavingsPercent = 10,
        });
        await t.Db.SaveChangesAsync();

        var svc = NewService(t.Db);

        await svc.SetMonthlyOverrideAsync(2026, 5, 500m);
        var pinned = await svc.GetBudgetAsync(2026, 5);
        Assert.True(pinned.IsManualBudget);
        Assert.Equal(500m, pinned.SpendBudget);

        await svc.SetMonthlyOverrideAsync(2026, 5, 640m); // re-pin updates in place
        Assert.Equal(640m, (await svc.GetBudgetAsync(2026, 5)).SpendBudget);

        await svc.SetMonthlyOverrideAsync(2026, 5, null); // clear -> computed default
        var computed = await svc.GetBudgetAsync(2026, 5);
        Assert.False(computed.IsManualBudget);
        Assert.Equal(900m, computed.SpendBudget);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SetMonthlyOverrideAsync(2026, 5, -1m));
    }

    // No income items at all: budget zeroes out instead of crashing, and everything spent
    // reads as over-budget.
    [Fact]
    public async Task Budget_NoPlan_NoRows_IsAllZeros()
    {
        using var t = new TestDb();
        var b = await NewService(t.Db).GetBudgetAsync(2026, 5);

        Assert.Equal(0m, b.PlannedIncome);
        Assert.Equal(0m, b.SpendBudget);
        Assert.Equal(0m, b.Spent);
        Assert.Equal(0m, b.Remaining);
        Assert.Empty(b.Macros);
    }
}
