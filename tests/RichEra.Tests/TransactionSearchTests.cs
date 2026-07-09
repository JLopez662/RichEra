using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// DB-backed tests for TransactionService.SearchAsync against a real (in-memory) SQLite database,
// so the EF Core query (ToLower()/Contains tokenization) is actually translated and executed.
public class TransactionSearchTests
{
    private sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _conn;
        public AppDbContext Db { get; }

        public TestDb()
        {
            _conn = new SqliteConnection("DataSource=:memory:");
            _conn.Open(); // keep the connection open so the :memory: schema/data persists
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
            Db = new AppDbContext(options);
            Db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Db.Dispose();
            _conn.Dispose();
        }
    }

    private static Guid Seed(AppDbContext db)
    {
        var acct = new Account { Name = "Popular Checking", Type = AccountType.Checking };
        db.Accounts.Add(acct);
        db.Transactions.AddRange(
            new Transaction { Date = new DateOnly(2026, 6, 1), AccountId = acct.Id, Amount = 21.88m, Direction = TransactionDirection.Expense, Note = "PURCHASE PAYPAL AFT P2P ON 06/05/26" },
            new Transaction { Date = new DateOnly(2026, 5, 10), AccountId = acct.Id, Amount = 73.73m, Direction = TransactionDirection.Expense, Note = "PURCHASE WM SUPERCENTER" },
            new Transaction { Date = new DateOnly(2026, 6, 9), AccountId = acct.Id, Amount = 15.80m, Direction = TransactionDirection.Expense, Note = "PURCHASE PAYPAL GABYHERRERA" }
        );
        db.SaveChanges();
        return acct.Id;
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_AndMatchesNoteSubstring()
    {
        using var t = new TestDb();
        Seed(t.Db);
        var svc = new TransactionService(t.Db);

        var results = await svc.SearchAsync("paypal"); // lowercase query vs uppercase notes

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains("PAYPAL", r.Note!));
    }

    [Fact]
    public async Task Search_MultipleTokens_RequireAllToMatch()
    {
        using var t = new TestDb();
        Seed(t.Db);
        var svc = new TransactionService(t.Db);

        var results = await svc.SearchAsync("paypal aft"); // both tokens only in one row

        var r = Assert.Single(results);
        Assert.Contains("AFT", r.Note!);
    }

    [Fact]
    public async Task Search_MatchesAccountName()
    {
        using var t = new TestDb();
        Seed(t.Db);
        var svc = new TransactionService(t.Db);

        var results = await svc.SearchAsync("popular"); // matches the account name, not the notes

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Search_ReturnsNewestFirst()
    {
        using var t = new TestDb();
        Seed(t.Db);
        var svc = new TransactionService(t.Db);

        var results = await svc.SearchAsync("purchase");

        Assert.Equal(3, results.Count);
        Assert.Equal(new DateOnly(2026, 6, 9), results[0].Date);
        Assert.Equal(new DateOnly(2026, 5, 10), results[^1].Date);
    }

    [Fact]
    public async Task Search_BlankQuery_ReturnsNothing()
    {
        using var t = new TestDb();
        Seed(t.Db);
        var svc = new TransactionService(t.Db);

        Assert.Empty(await svc.SearchAsync("   "));
    }

    [Fact]
    public async Task GetActiveMonths_ReturnsDistinctMonthsWithData()
    {
        using var t = new TestDb();
        Seed(t.Db); // seeded: May 2026 (x1), Jun 2026 (x2)
        var svc = new TransactionService(t.Db);

        var months = await svc.GetActiveMonthsAsync();

        Assert.Equal(2, months.Count);
        Assert.Contains((2026, 5), months);
        Assert.Contains((2026, 6), months);
    }
}
