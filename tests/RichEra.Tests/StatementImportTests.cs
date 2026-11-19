using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// DB-backed tests for StatementImportService's matching + balance behavior against in-memory
// SQLite, driving it through real OFX text so the parser stays in the loop.
public class StatementImportTests
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

    private static StatementImportService Service(TestDb t) => new(t.Db, new OfxParser());

    // Synthetic BPPR-dialect OFX (unclosed leaf tags, LEDGERBAL 0 like the real exports).
    // Amount is signed: negative = debit/expense, positive = credit/income.
    private static Stream Ofx(params (string FitId, string Date, decimal Amount, string Name)[] rows)
    {
        var sb = new StringBuilder("""
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
            """);
        sb.AppendLine();
        foreach (var r in rows)
        {
            sb.AppendLine("<STMTTRN>");
            sb.AppendLine($"<TRNTYPE>{(r.Amount < 0 ? "DEBIT" : "CREDIT")}");
            sb.AppendLine($"<DTPOSTED>{r.Date}040000");
            sb.AppendLine($"<TRNAMT>{r.Amount:0.00}");
            sb.AppendLine($"<FITID>{r.FitId}");
            sb.AppendLine($"<NAME>{r.Name}");
            sb.AppendLine("</STMTTRN>");
        }
        sb.AppendLine("""
            </BANKTRANLIST>
            <LEDGERBAL>
            <BALAMT>0
            <DTASOF>20260702010535
            </LEDGERBAL>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """);
        return new MemoryStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    private static async Task<Account> SeedAccountAsync(TestDb t, decimal balance)
    {
        var acct = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = balance };
        t.Db.Accounts.Add(acct);
        await t.Db.SaveChangesAsync();
        return acct;
    }

    [Fact]
    public async Task Merge_UpdatesBalance_ByNetOfNewRows()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);

        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260616", -173.94m, "PURCHASE ABC HCOA CAYEY"),
            ("F2", "20260617", 190.00m, "TRANF ATHM SONIA ORTIZ")));

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(16.06m, result.BalanceDelta);
        Assert.Equal(1016.06m, result.NewBalance);
        Assert.Equal(1016.06m, (await t.Db.Accounts.SingleAsync()).CurrentBalance);
    }

    [Fact]
    public async Task Merge_WithUpdateBalanceOff_LeavesBalance()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);

        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260616", -50.00m, "PURCHASE SOMEWHERE")), updateBalance: false);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0m, result.BalanceDelta);
        Assert.Equal(1000m, (await t.Db.Accounts.SingleAsync()).CurrentBalance);
    }

    [Fact]
    public async Task Merge_Reimport_IsIdempotent_AndDoesNotMoveBalanceTwice()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);
        var svc = Service(t);

        await svc.CommitAsync(acct.Id, Ofx(("F1", "20260616", -100.00m, "PURCHASE X")));
        var second = await svc.CommitAsync(acct.Id, Ofx(("F1", "20260616", -100.00m, "PURCHASE X")));

        Assert.Equal(0, second.ImportedCount);
        Assert.Equal(1, second.SkippedCount);
        Assert.Equal(0m, second.BalanceDelta);
        Assert.Equal(900m, (await t.Db.Accounts.SingleAsync()).CurrentBalance);
        Assert.Equal(1, await t.Db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Merge_AdoptsManualTransaction_SameAmountCloseDateSimilarTitle()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m); // balance already reflects the manual entry
        var manual = new Transaction
        {
            Date = new DateOnly(2026, 6, 27),
            AccountId = acct.Id,
            Amount = 34.01m,
            Direction = TransactionDirection.Expense,
            Note = "Wendy's",
        };
        t.Db.Transactions.Add(manual);
        await t.Db.SaveChangesAsync();

        // Bank posts it two days later with its own noisy description.
        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260629", -34.01m, "PURCHASE WENDY S PUNTA CANA ON 06/27/26 HIGUEY DO")));

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.MatchedExistingCount);
        Assert.Equal(0m, result.BalanceDelta); // was already counted when entered manually

        var only = await t.Db.Transactions.SingleAsync();
        Assert.Equal(manual.Id, only.Id);
        Assert.Equal("F1", only.ExternalId);
        Assert.Equal("ofx", only.ImportSource);
        Assert.Equal(new DateOnly(2026, 6, 27), only.Date); // user's date kept
    }

    [Fact]
    public async Task Merge_DoesNotAdopt_WhenTitleIsUnrelated()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 18),
            AccountId = acct.Id,
            Amount = 20.00m,
            Direction = TransactionDirection.Expense,
            Note = "Groceries",
        });
        await t.Db.SaveChangesAsync();

        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260622", -20.00m, "PURCHASE PAYPAL SANTOSONTROVO ON 06/18/26")));

        Assert.Equal(1, result.ImportedCount); // inserted as its own transaction
        Assert.Equal(0, result.MatchedExistingCount);
        Assert.Equal(-20.00m, result.BalanceDelta);
        Assert.Equal(2, await t.Db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Merge_LinksRecurringRule_AndOccurrenceShowsPaid()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);
        var rule = new Item
        {
            Name = "Planet Fitness",
            Amount = 27.87m,
            Direction = TransactionDirection.Expense,
            Frequency = RepeatFrequency.Monthly,
            StartDate = new DateOnly(2026, 6, 15),
            AccountId = acct.Id,
        };
        t.Db.Items.Add(rule);
        await t.Db.SaveChangesAsync();

        // Bank charges two days after the projected occurrence.
        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260617", -27.87m, "EFT PMT PLANET FITNESS C IClub Fees XXXXXXXXXXX8540")));

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.RecurringPaidCount);
        Assert.Equal(-27.87m, result.BalanceDelta);

        var txn = await t.Db.Transactions.SingleAsync();
        Assert.Equal(rule.Id, txn.ItemId);

        var checks = await new IncomeProjectionService(t.Db)
            .GetPaychecksAsync(rule.Id, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        var occ = Assert.Single(checks);
        Assert.Equal(new DateOnly(2026, 6, 15), occ.Date);
        Assert.True(occ.IsReceived);
        Assert.Equal(27.87m, occ.ActualAmount);
        Assert.Equal(new DateOnly(2026, 6, 17), occ.ActualDate);
    }

    [Fact]
    public async Task Merge_UpdatesPreconfirmedRecurring_ToBankActuals()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);
        var rule = new Item
        {
            Name = "Planet Fitness",
            Amount = 25.00m,
            Direction = TransactionDirection.Expense,
            Frequency = RepeatFrequency.Monthly,
            StartDate = new DateOnly(2026, 6, 15),
            AccountId = acct.Id,
        };
        t.Db.Items.Add(rule);
        // User confirmed the occurrence early at the projected amount; balance already took −25.
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 15),
            AccountId = acct.Id,
            Amount = 25.00m,
            Direction = TransactionDirection.Expense,
            ItemId = rule.Id,
            Note = "Planet Fitness",
        });
        await t.Db.SaveChangesAsync();

        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260617", -27.87m, "EFT PMT PLANET FITNESS C IClub Fees")));

        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(1, result.MatchedExistingCount);
        Assert.Equal(1, result.RecurringPaidCount);
        Assert.Equal(-2.87m, result.BalanceDelta); // only the correction moves the balance
        Assert.Equal(997.13m, (await t.Db.Accounts.SingleAsync()).CurrentBalance);

        var txn = await t.Db.Transactions.SingleAsync();
        Assert.Equal(27.87m, txn.Amount);
        Assert.Equal(new DateOnly(2026, 6, 17), txn.Date);
        Assert.Equal("F1", txn.ExternalId);
        Assert.Equal(rule.Id, txn.ItemId);
    }

    [Fact]
    public async Task Replace_AdjustsBalance_ByImportedMinusDeleted()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 1000m);
        // Existing data nets to −200.
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 1),
            AccountId = acct.Id,
            Amount = 200m,
            Direction = TransactionDirection.Expense,
            Note = "Old row",
        });
        await t.Db.SaveChangesAsync();

        // File nets to −150 → balance moves by −150 − (−200) = +50.
        var result = await Service(t).CommitAsync(acct.Id, Ofx(
            ("F1", "20260616", -150.00m, "PURCHASE X")),
            Application.Dtos.StatementImportMode.Replace);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(50m, result.BalanceDelta);
        Assert.Equal(1050m, (await t.Db.Accounts.SingleAsync()).CurrentBalance);
    }

    [Fact]
    public async Task Preview_ReportsKinds_AndProjectedBalance()
    {
        using var t = new TestDb();
        var acct = await SeedAccountAsync(t, 500m);
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 27),
            AccountId = acct.Id,
            Amount = 34.01m,
            Direction = TransactionDirection.Expense,
            Note = "Wendy's",
        });
        await t.Db.SaveChangesAsync();

        var preview = await Service(t).PreviewAsync(acct.Id, Ofx(
            ("F1", "20260629", -34.01m, "PURCHASE WENDY S PUNTA CANA"),
            ("F2", "20260630", -20.00m, "PURCHASE OPENAI CHATGPT SUBSCR")));

        Assert.Equal(1, preview.NewCount);
        Assert.Equal(1, preview.MatchedExistingCount);
        Assert.Equal(-20.00m, preview.NetImported); // adopted row doesn't move the balance
        Assert.Equal(500m, preview.CurrentBalance);
        Assert.Equal(480m, preview.ProjectedBalance);

        Assert.Contains(preview.Rows, r => r.FitId == "F1" && r.Kind == Application.Dtos.ImportRowKind.MatchesExisting);
        Assert.Contains(preview.Rows, r => r.FitId == "F2" && r.Kind == Application.Dtos.ImportRowKind.New);
    }

    [Theory]
    // User's short note vs the bank's noisy description.
    [InlineData("Wendy's", "PURCHASE WENDY S PUNTA CANA ON 06/27/26 HIGUEY DO", true)]
    [InlineData("Planet Fitness", "EFT PMT PLANET FITNESS C IClub Fees XXXXXXXXXXX8540", true)]
    [InlineData("Walmart", "PURCHASE WAL-MART 2721 ON 06/28/26 CAYEY", true)]
    [InlineData("Contoso Systems", "EFT DEPOSIT CONTOSO SYSTEMS PAYROLL XXXXXXXXXXX762D", true)]
    [InlineData("Groceries", "PURCHASE PAYPAL SANTOSONTROVO ON 06/18/26", false)]
    [InlineData("", "PURCHASE WENDY S PUNTA CANA", false)]      // empty note never matches
    [InlineData("PR", "PURCHASE SEGAFREDO ON 06/27/26 HIGUEY PR", false)] // short tokens can't carry a match
    public void Matcher_Similar(string a, string b, bool expected)
        => Assert.Equal(expected, TransactionMatcher.Similar(a, b));
}
