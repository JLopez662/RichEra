using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// DB-backed tests for the savings-goal feature against in-memory SQLite.
public class SavingsTests
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
    public async Task Plan_DerivesSavedFromReceivedPaychecksTimesPercent()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        t.Db.Accounts.Add(checking);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.TrackOnly, null));

        // Receive one paycheck today.
        await new IncomeProjectionService(t.Db).ConfirmPaycheckAsync(income.Id, today, 1000m);

        var plan = await savings.GetPlanAsync();

        Assert.True(plan.HasGoal);
        Assert.Equal(300m, plan.SavedSoFar);   // 1000 × 30%
        Assert.Equal(4700m, plan.Remaining);
        Assert.Equal(6, plan.PercentComplete); // 300 / 5000
        var c = Assert.Single(plan.Contributors);
        Assert.Equal(30, c.SavingsPercent);
        Assert.Equal(300m, c.PerPaycheck);
    }

    [Fact]
    public async Task UpsertGoal_MoveMode_RequiresLinkedAccount()
    {
        using var t = new TestDb();
        var savings = new SavingsService(t.Db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Goal", 100m, SavingsMode.MoveToSavings, null)));
    }

    [Fact]
    public async Task ReceivePaycheck_MoveMode_TransfersSavedPercentToSavingsAccount()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));

        await new IncomeProjectionService(t.Db).ConfirmPaycheckAsync(income.Id, today, 1000m);

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(700m, chk.CurrentBalance);  // +1000 income − 300 moved to savings
        Assert.Equal(300m, sav.CurrentBalance);  // received the moved 30%

        // Move-mode progress reflects the actually-moved money.
        var plan = await savings.GetPlanAsync();
        Assert.Equal(300m, plan.SavedSoFar);
    }

    [Fact]
    public async Task DeletingPaycheck_UnwindsItsSavingsMove()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));
        await new IncomeProjectionService(t.Db).ConfirmPaycheckAsync(income.Id, today, 1000m);

        var paycheck = await t.Db.Transactions.AsNoTracking().FirstAsync(x => x.ItemId == income.Id);
        await new TransactionService(t.Db).DeleteAsync(paycheck.Id);

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(0m, chk.CurrentBalance);  // income reversed AND the move reversed
        Assert.Equal(0m, sav.CurrentBalance);
        Assert.Empty(await t.Db.Transactions.AsNoTracking().Where(x => x.SavingsSourceTransactionId != null).ToListAsync());
    }

    [Fact]
    public async Task ReConfirmingPaycheck_AdjustsTheSavingsMove()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));
        var proj = new IncomeProjectionService(t.Db);
        await proj.ConfirmPaycheckAsync(income.Id, today, 1000m); // moves 300
        await proj.ConfirmPaycheckAsync(income.Id, today, 2000m); // re-confirm: should move 600 now

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(1400m, chk.CurrentBalance);  // +2000 income − 600 moved
        Assert.Equal(600m, sav.CurrentBalance);
        Assert.Equal(2, await t.Db.Transactions.CountAsync(x => x.SavingsSourceTransactionId != null)); // one fresh pair
        Assert.Equal(600m, (await savings.GetPlanAsync()).SavedSoFar);
    }

    [Fact]
    public async Task Plan_UnreachableTarget_ReturnsNoProjectionWithoutCrashing()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        t.Db.Accounts.Add(checking);
        // $1/mo income saving 1% → $0.01/mo against a $5,000 target = a ~500,000-month horizon.
        t.Db.Items.Add(new Item { Name = "Tiny", Amount = 1m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 1 });
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Big", 5000m, SavingsMode.TrackOnly, null));

        var plan = await savings.GetPlanAsync(); // must not throw (DateOnly.AddDays would overflow)
        Assert.Null(plan.ProjectedCompletion);
    }

    [Fact]
    public async Task RecreatedGoal_SameAccount_DoesNotInheritOldGoalsMoves()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Goal A", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));
        await new IncomeProjectionService(t.Db).ConfirmPaycheckAsync(income.Id, today, 1000m); // moves 300 toward Goal A

        await savings.ClearGoalAsync();                                                       // archive A
        await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Goal B", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id)); // reuse same account

        // B is brand new — it must NOT inherit A's $300 of moves even though they share an account.
        Assert.Equal(0m, (await savings.GetPlanAsync()).SavedSoFar);
    }

    [Fact]
    public async Task PostDeposit_TrackOnly_BumpsTallyWithoutMovingMoney()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        t.Db.Accounts.Add(checking);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.TrackOnly, null));
        await new IncomeProjectionService(t.Db).ConfirmPaycheckAsync(income.Id, today, 1000m); // notional 300, checking = 1000

        // A track-only deposit is a flat tally bump — it must NOT be multiplied by any % and must move no money.
        await savings.PostDepositAsync(goal.Id, 200m, today, fromAccountId: checking.Id);

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        Assert.Equal(1000m, chk.CurrentBalance);                 // funding account untouched (no money moved)
        Assert.Equal(500m, (await savings.GetPlanAsync()).SavedSoFar); // 300 notional + 200 manual
        Assert.Single(await t.Db.SavingsContributions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostDeposit_MoveMode_TransfersAndCountsExactlyOnce()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 1000m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));

        await savings.PostDepositAsync(goal.Id, 200m, today, fromAccountId: checking.Id);

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(800m, chk.CurrentBalance);   // funded the deposit
        Assert.Equal(200m, sav.CurrentBalance);   // received it
        Assert.Equal(200m, (await savings.GetPlanAsync()).SavedSoFar);
        // Exactly one Income leg counts toward the goal (the expense leg is excluded by the tally filter).
        Assert.Equal(1, await t.Db.Transactions.CountAsync(x => x.SavingsGoalId == goal.Id && x.Direction == TransactionDirection.Income));
        // A manual deposit is NOT tied to a paycheck — leaving this null is what protects it from re-sync/unwind.
        Assert.Empty(await t.Db.Transactions.AsNoTracking().Where(x => x.SavingsGoalId == goal.Id && x.SavingsSourceTransactionId != null).ToListAsync());
    }

    [Fact]
    public async Task PostDeposit_MoveMode_SurvivesPaycheckReSync()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 0m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        var income = new Item { Name = "Payroll", Amount = 1000m, Direction = TransactionDirection.Income, Frequency = RepeatFrequency.Monthly, StartDate = today, AccountId = checking.Id, SavingsPercent = 30 };
        t.Db.Items.Add(income);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));
        var proj = new IncomeProjectionService(t.Db);

        await proj.ConfirmPaycheckAsync(income.Id, today, 1000m);          // moves 300
        await savings.PostDepositAsync(goal.Id, 200m, today, checking.Id); // manual +200 → 500 total
        await proj.ConfirmPaycheckAsync(income.Id, today, 1000m);          // re-sync: must not disturb the manual deposit

        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(500m, sav.CurrentBalance);                            // 300 auto + 200 manual, unchanged by re-sync
        Assert.Equal(500m, (await savings.GetPlanAsync()).SavedSoFar);
    }

    [Fact]
    public async Task PostDeposit_MoveMode_DeleteUnwindsBalancesAndProgress()
    {
        using var t = new TestDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var checking = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 1000m };
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.AddRange(checking, savingsAcct);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));
        await savings.PostDepositAsync(goal.Id, 200m, today, checking.Id);

        var leg = await t.Db.Transactions.AsNoTracking().FirstAsync(x => x.SavingsGoalId == goal.Id && x.Direction == TransactionDirection.Income);
        await new TransactionService(t.Db).DeleteAsync(leg.Id);

        var chk = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == checking.Id);
        var sav = await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == savingsAcct.Id);
        Assert.Equal(1000m, chk.CurrentBalance);  // both legs reversed
        Assert.Equal(0m, sav.CurrentBalance);
        Assert.Equal(0m, (await savings.GetPlanAsync()).SavedSoFar);
    }

    [Fact]
    public async Task PostDeposit_MoveMode_RequiresFundingAccount()
    {
        using var t = new TestDb();
        var savingsAcct = new Account { Name = "Savings", Type = AccountType.Savings, CurrentBalance = 0m };
        t.Db.Accounts.Add(savingsAcct);
        await t.Db.SaveChangesAsync();

        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.MoveToSavings, savingsAcct.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => savings.PostDepositAsync(goal.Id, 200m, DateOnly.FromDateTime(DateTime.Today), fromAccountId: null));
    }

    [Fact]
    public async Task PostDeposit_RejectsNonPositiveAmount()
    {
        using var t = new TestDb();
        var savings = new SavingsService(t.Db);
        var goal = await savings.UpsertGoalAsync(new SavingsGoalUpsertDto(null, "Emergency", 5000m, SavingsMode.TrackOnly, null));

        await Assert.ThrowsAsync<ArgumentException>(() => savings.PostDepositAsync(goal.Id, 0m, DateOnly.FromDateTime(DateTime.Today)));
    }
}
