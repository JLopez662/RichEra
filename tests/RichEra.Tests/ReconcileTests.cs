using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// Balance reconciliation: SetBalanceAsync's adjustment rows are marked "reconcile" so they
// keep the ledger honest without polluting cash-flow stats or statement-import matching.
public class ReconcileTests
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

    private static async Task<Account> SeedAccountAsync(TestDb t, decimal balance)
    {
        var acct = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = balance };
        t.Db.Accounts.Add(acct);
        await t.Db.SaveChangesAsync();
        return acct;
    }

    [Fact]
    public async Task SetBalance_RecordsMarkedAdjustment_AndSetsBalance()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 800m);

        var result = await new AccountService(t.Db)
            .SetBalanceAsync(new SetBalanceDto(acct.Id, 757.24m, RecordAdjustment: true, Note: null));

        Assert.Equal(757.24m, result.CurrentBalance);
        var adj = await t.Db.Transactions.SingleAsync();
        Assert.Equal(42.76m, adj.Amount);
        Assert.Equal(TransactionDirection.Expense, adj.Direction); // balance went down
        Assert.Equal("reconcile", adj.ImportSource);
        Assert.Equal("Balance adjustment", adj.Note);
    }

    [Fact]
    public async Task SetBalance_ZeroDelta_RecordsNothing()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 500m);

        await new AccountService(t.Db)
            .SetBalanceAsync(new SetBalanceDto(acct.Id, 500m, RecordAdjustment: true, Note: null));

        Assert.Equal(0, await t.Db.Transactions.CountAsync());
    }

    [Fact]
    public async Task MonthlySummary_ExcludesReconcileAdjustments()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 500m);
        var today = DateOnly.FromDateTime(DateTime.Today);
        t.Db.Transactions.AddRange(
            new Transaction { Date = today, AccountId = acct.Id, Amount = 100m, Direction = TransactionDirection.Expense, Note = "Groceries" },
            new Transaction { Date = today, AccountId = acct.Id, Amount = 40m, Direction = TransactionDirection.Income, Note = "Balance adjustment", ImportSource = "reconcile" });
        await t.Db.SaveChangesAsync();

        var summary = await new CashFlowService(t.Db).GetMonthlySummaryAsync(today.Year, today.Month);

        Assert.Equal(0m, summary.Income);      // the +40 reconcile row doesn't count as income
        Assert.Equal(100m, summary.Expenses);
    }

    [Fact]
    public async Task Import_NeverAdoptsReconcileAdjustments()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 500m);
        // A reconcile adjustment that happens to look exactly like an incoming bank row
        // (same amount, same date, note similar to the bank text) must still not be adopted.
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 29),
            AccountId = acct.Id,
            Amount = 34.01m,
            Direction = TransactionDirection.Expense,
            Note = "Wendy's Punta Cana",
            ImportSource = "reconcile",
        });
        await t.Db.SaveChangesAsync();

        var ofx = """
            OFXHEADER:100
            DATA:OFXSGML
            VERSION:102
            SECURITY:NONE
            ENCODING:USASCII
            CHARSET:1252
            COMPRESSION:NONE
            OLDFILEUID:NONE
            NEWFILEUID:NONE

            <OFX>
            <BANKMSGSRSV1>
            <STMTTRNRS>
            <STMTRS>
            <CURDEF>USD
            <BANKACCTFROM>
            <BANKID>001
            <ACCTID>000111222
            <ACCTTYPE>CHECKING
            </BANKACCTFROM>
            <BANKTRANLIST>
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260629040000
            <TRNAMT>-34.01
            <FITID>F1
            <NAME>PURCHASE WENDY S PUNTA CANA ON 06/27/26 HIGUEY DO
            </STMTTRN>
            </BANKTRANLIST>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(ofx));
        var result = await new StatementImportService(t.Db, new OfxParser()).CommitAsync(acct.Id, stream);

        Assert.Equal(1, result.ImportedCount);          // inserted as its own row
        Assert.Equal(0, result.MatchedExistingCount);   // the adjustment was left alone
        Assert.Equal(2, await t.Db.Transactions.CountAsync());
        var adj = await t.Db.Transactions.SingleAsync(x => x.ImportSource == "reconcile");
        Assert.Null(adj.ExternalId);
    }
}
