using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// ItemService.GetStatsAsync / GetLastLoggedAsync — the History block of the Money Facts
// label and the editor's autofill-from-history — plus the quick-log path through
// TransactionService.UpsertAsync (ItemId + Quantity semantics).
public class ItemStatsTests
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

    private static (Account Acct, Item Item) Seed(AppDbContext db)
    {
        var acct = new Account { Name = "Checking", Type = AccountType.Checking };
        var item = new Item { Name = "Gas — Shell", Amount = 38.20m, Direction = TransactionDirection.Expense, AccountId = acct.Id, UnitName = "tank" };
        db.Accounts.Add(acct);
        db.Items.Add(item);
        return (acct, item);
    }

    [Fact]
    public async Task Stats_ComputeHistory_TypicalPerServing_AndCadenceGuess()
    {
        using var t = new TestDb();
        var (acct, item) = Seed(t.Db);

        // Biweekly-ish logs; the middle one is a double fill-up (2 servings, total 80),
        // so the per-serving median must normalize it to 40, not 80.
        var d0 = new DateOnly(2026, 5, 1);
        t.Db.Transactions.AddRange(
            new Transaction { Date = d0, AccountId = acct.Id, Amount = 38m, Direction = TransactionDirection.Expense, ItemId = item.Id },
            new Transaction { Date = d0.AddDays(14), AccountId = acct.Id, Amount = 80m, Quantity = 2m, Direction = TransactionDirection.Expense, ItemId = item.Id },
            new Transaction { Date = d0.AddDays(28), AccountId = acct.Id, Amount = 42m, Direction = TransactionDirection.Expense, ItemId = item.Id });
        await t.Db.SaveChangesAsync();

        var st = await new ItemService(t.Db).GetStatsAsync(item.Id);

        Assert.Equal(3, st.TimesLogged);
        Assert.Equal(160m, st.LifetimeTotal);          // row totals: 38 + 80 + 42
        Assert.Equal(d0, st.FirstLogged);
        Assert.Equal(d0.AddDays(28), st.LastLogged);
        Assert.Equal(40m, st.TypicalAmount);           // median of per-serving 38, 40, 42
        Assert.Equal(RepeatFrequency.Biweekly, st.GuessedFrequency);
    }

    [Fact]
    public async Task Stats_NoLogs_AreEmpty_AndIrregularGapsGuessNothing()
    {
        using var t = new TestDb();
        var (acct, item) = Seed(t.Db);
        await t.Db.SaveChangesAsync();

        var empty = await new ItemService(t.Db).GetStatsAsync(item.Id);
        Assert.Equal(0, empty.TimesLogged);
        Assert.Null(empty.TypicalAmount);
        Assert.Null(empty.GuessedFrequency);

        // Erratic gaps (2 and 50 days) → no cadence call.
        var d0 = new DateOnly(2026, 5, 1);
        t.Db.Transactions.AddRange(
            new Transaction { Date = d0, AccountId = acct.Id, Amount = 10m, Direction = TransactionDirection.Expense, ItemId = item.Id },
            new Transaction { Date = d0.AddDays(2), AccountId = acct.Id, Amount = 10m, Direction = TransactionDirection.Expense, ItemId = item.Id },
            new Transaction { Date = d0.AddDays(52), AccountId = acct.Id, Amount = 10m, Direction = TransactionDirection.Expense, ItemId = item.Id });
        await t.Db.SaveChangesAsync();

        var st = await new ItemService(t.Db).GetStatsAsync(item.Id);
        Assert.Null(st.GuessedFrequency);
    }

    [Fact]
    public async Task LastLogged_OneQueryPerLibrary_MapsEveryItem()
    {
        using var t = new TestDb();
        var (acct, gas) = Seed(t.Db);
        var coffee = new Item { Name = "Coffee", Amount = 6.5m, Direction = TransactionDirection.Expense, AccountId = acct.Id };
        t.Db.Items.Add(coffee);
        t.Db.Transactions.AddRange(
            new Transaction { Date = new DateOnly(2026, 6, 1), AccountId = acct.Id, Amount = 38m, Direction = TransactionDirection.Expense, ItemId = gas.Id },
            new Transaction { Date = new DateOnly(2026, 6, 9), AccountId = acct.Id, Amount = 40m, Direction = TransactionDirection.Expense, ItemId = gas.Id },
            new Transaction { Date = new DateOnly(2026, 6, 5), AccountId = acct.Id, Amount = 13m, Direction = TransactionDirection.Expense, ItemId = coffee.Id });
        await t.Db.SaveChangesAsync();

        var map = await new ItemService(t.Db).GetLastLoggedAsync();

        Assert.Equal(new DateOnly(2026, 6, 9), map[gas.Id]);
        Assert.Equal(new DateOnly(2026, 6, 5), map[coffee.Id]);
    }

    [Fact]
    public async Task QuickLog_Upsert_PersistsItemLink_AndEditKeepsIt()
    {
        using var t = new TestDb();
        var (acct, item) = Seed(t.Db);
        await t.Db.SaveChangesAsync();
        var svc = new TransactionService(t.Db);

        // Quick log: 2 servings, total amount, linked to the item.
        var created = await svc.UpsertAsync(new RichEra.Application.Dtos.TransactionUpsertDto(
            null, new DateOnly(2026, 7, 9), acct.Id, null, 76.40m, TransactionDirection.Expense,
            item.Name, item.Id, 2m));

        Assert.Equal(item.Id, created.ItemId);
        Assert.Equal(2m, created.Quantity);
        Assert.Equal("Gas — Shell", created.ItemName);   // projection pulls the item's name

        // Editing the row (amount fix) must keep the link and the serving count.
        var edited = await svc.UpsertAsync(new RichEra.Application.Dtos.TransactionUpsertDto(
            created.Id, created.Date, acct.Id, null, 75.00m, TransactionDirection.Expense,
            created.Note, null /* edits never re-link */, created.Quantity));

        Assert.Equal(item.Id, edited.ItemId);
        Assert.Equal(2m, edited.Quantity);
        Assert.Equal(75.00m, edited.Amount);

        // Balance applied once on create, then adjusted by the delta on edit.
        var balance = (await t.Db.Accounts.AsNoTracking().FirstAsync(a => a.Id == acct.Id)).CurrentBalance;
        Assert.Equal(-75.00m, balance);
    }
}
