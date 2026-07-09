using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

// Backup round-trip + legacy-restore tests. The v1 format predates the Money Log rename
// (IncomeSources/incomeSourceId, mandatory schedule, no quantity) and must keep restoring —
// every auto-backup a user already has on disk is v1.
public class DataPortabilityTests
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
    public async Task ExportThenImport_RoundTrips_ItemsMicrosAndQuantity()
    {
        using var t = new TestDb();
        var svc = new DataPortabilityService(t.Db);

        var acct = new Account { Name = "Checking", Type = AccountType.Checking, CurrentBalance = 250m };
        var cat = new Category { Name = "Housing", Kind = CategoryKind.Expense, Bucket = MacroBucket.Needs };
        var visa = new Item
        {
            Name = "Chase Visa — payment", Counterparty = "Chase", Amount = 180m,
            Direction = TransactionDirection.Expense, Bucket = MacroBucket.Debt, UnitName = "payment",
            Frequency = RepeatFrequency.Monthly, StartDate = new DateOnly(2026, 6, 7), AccountId = acct.Id,
            AprPct = 24.99m, Interest = InterestModel.CompoundDaily, PrincipalBalance = 4320m,
            PlanPaymentsTotal = 48, PlanPaymentsMade = 16,
        };
        t.Db.Accounts.Add(acct);
        t.Db.Categories.Add(cat);
        t.Db.Items.Add(visa);
        t.Db.Transactions.Add(new Transaction
        {
            Date = new DateOnly(2026, 6, 7), AccountId = acct.Id, Amount = 360m,
            Direction = TransactionDirection.Expense, ItemId = visa.Id, Quantity = 2m,
        });
        t.Db.Settings.Add(new Setting { Key = "budget.override.2026-06", Value = "1500" });
        await t.Db.SaveChangesAsync();

        var json = await svc.ExportAsJsonAsync();
        Assert.Contains(AppDataExport.CurrentSchema, json);

        // Drift the DB so a successful restore is provably a replace, not a no-op.
        t.Db.Items.Add(new Item { Name = "Stray", Amount = 1m, Direction = TransactionDirection.Expense, AccountId = acct.Id });
        await t.Db.SaveChangesAsync();

        var result = await svc.ImportFromJsonAsync(json);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Counts!.Items);
        var item = Assert.Single(await t.Db.Items.AsNoTracking().ToListAsync());
        Assert.Equal("Chase Visa — payment", item.Name);
        Assert.Equal(24.99m, item.AprPct);
        Assert.Equal(InterestModel.CompoundDaily, item.Interest);
        Assert.Equal(16, item.PlanPaymentsMade);
        Assert.Equal(MacroBucket.Debt, item.Bucket);
        var txn = Assert.Single(await t.Db.Transactions.AsNoTracking().ToListAsync());
        Assert.Equal(item.Id, txn.ItemId);
        Assert.Equal(2m, txn.Quantity);
        var setting = Assert.Single(await t.Db.Settings.AsNoTracking().ToListAsync());
        Assert.Equal("budget.override.2026-06", setting.Key);
        var category = Assert.Single(await t.Db.Categories.AsNoTracking().ToListAsync());
        Assert.Equal(MacroBucket.Needs, category.Bucket);
    }

    [Fact]
    public async Task Import_V1Backup_UpgradesIncomeSourcesAndLinks()
    {
        using var t = new TestDb();
        var svc = new DataPortabilityService(t.Db);

        // A faithful v1 file as the old exporter wrote it: camelCase keys, string enums,
        // incomeSources with a mandatory schedule, transactions with incomeSourceId, no quantity.
        const string v1 = """
        {
          "schema": "richera-export/v1",
          "exportedAtUtc": "2026-06-01T12:00:00Z",
          "database": {
            "accounts": [
              { "id": "11111111-1111-1111-1111-111111111111", "name": "Checking", "type": "Checking",
                "openingBalance": 0, "currentBalance": 100, "createdUtc": "2026-01-01T00:00:00Z", "isArchived": false }
            ],
            "categories": [
              { "id": "55555555-5555-5555-5555-555555555555", "name": "Salary", "kind": "Income", "isArchived": false }
            ],
            "incomeSources": [
              { "id": "22222222-2222-2222-2222-222222222222", "name": "Payroll", "amount": 1625,
                "direction": "Income", "frequency": "Biweekly", "startDate": "2026-01-02",
                "accountId": "11111111-1111-1111-1111-111111111111",
                "categoryId": "55555555-5555-5555-5555-555555555555",
                "isArchived": false, "createdUtc": "2026-01-01T00:00:00Z", "savingsPercent": 10 }
            ],
            "transactions": [
              { "id": "33333333-3333-3333-3333-333333333333", "date": "2026-01-02",
                "accountId": "11111111-1111-1111-1111-111111111111", "amount": 1625,
                "direction": "Income", "createdUtc": "2026-01-02T00:00:00Z",
                "incomeSourceId": "22222222-2222-2222-2222-222222222222" }
            ]
          }
        }
        """;

        var result = await svc.ImportFromJsonAsync(v1);

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Counts!.Items);

        var item = Assert.Single(await t.Db.Items.AsNoTracking().ToListAsync());
        Assert.Equal("Payroll", item.Name);
        Assert.Equal(RepeatFrequency.Biweekly, item.Frequency);
        Assert.Equal(new DateOnly(2026, 1, 2), item.StartDate);
        Assert.Equal(10, item.SavingsPercent);
        Assert.Null(item.Bucket);                    // v1 predates buckets
        Assert.Equal(InterestModel.None, item.Interest);

        var txn = Assert.Single(await t.Db.Transactions.AsNoTracking().ToListAsync());
        Assert.Equal(item.Id, txn.ItemId);           // incomeSourceId carried across
        Assert.Equal(1m, txn.Quantity);              // absent in v1 -> defaults to one serving
    }

    [Fact]
    public async Task Import_UnknownSchema_IsRejected_AndKeepsData()
    {
        using var t = new TestDb();
        var svc = new DataPortabilityService(t.Db);
        t.Db.Accounts.Add(new Account { Name = "Keep me", Type = AccountType.Cash });
        await t.Db.SaveChangesAsync();

        var result = await svc.ImportFromJsonAsync("""{ "schema": "richera-export/v99", "database": {} }""");

        Assert.False(result.Success);
        Assert.Contains("v99", result.Message);
        Assert.Single(await t.Db.Accounts.AsNoTracking().ToListAsync()); // untouched
    }
}
