using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RichEra.Application.Dtos;
using RichEra.Domain.Entities;
using RichEra.Infrastructure.Persistence;
using RichEra.Infrastructure.Services;
using Xunit;

namespace RichEra.Tests;

file sealed class TestDb : IDisposable
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

// Saved-cart bundles: create, live-title resolution, availability, archive.
public class BundleServiceTests
{
    [Fact]
    public async Task Create_List_ResolvesLiveTitles_AndAvailability()
    {
        using var t = new TestDb();
        var acct = new Account { Name = "Checking", Type = AccountType.Checking };
        var card = new Account { Name = "Chase Visa", Type = AccountType.CreditCard, CurrentBalance = -500m };
        var netflix = new Item { Name = "Netflix", Amount = 22.99m, Direction = TransactionDirection.Expense, AccountId = acct.Id };
        t.Db.Accounts.AddRange(acct, card);
        t.Db.Items.Add(netflix);
        await t.Db.SaveChangesAsync();

        var svc = new BundleService(t.Db);
        var created = await svc.CreateAsync("Payday routine", new[]
        {
            new BundleLineInput("expense", netflix.Id, 22.99m, null, "Netflix"),
            new BundleLineInput("card", card.Id, 150m, acct.Id, "Chase Visa"),
            new BundleLineInput("expense", Guid.NewGuid(), 0m, null, "zero line — dropped"),
        });

        Assert.Equal(2, created.LineCount);          // the zero line was noise
        Assert.Equal(172.99m, created.Total);

        // Rename the item: the bundle line reads the LIVE name, not the snapshot.
        (await t.Db.Items.FirstAsync(i => i.Id == netflix.Id)).Name = "Netflix Premium";
        await t.Db.SaveChangesAsync();

        var listed = Assert.Single(await svc.ListAsync());
        var expenseLine = Assert.Single(listed.Lines, l => l.Kind == "expense");
        Assert.Equal("Netflix Premium", expenseLine.Title);
        Assert.True(expenseLine.IsAvailable);
        var cardLine = Assert.Single(listed.Lines, l => l.Kind == "card");
        Assert.Equal("Checking", cardLine.FromName); // funding account resolved for the cart

        // Archive the item: its line goes unavailable, snapshot title survives for display.
        (await t.Db.Items.FirstAsync(i => i.Id == netflix.Id)).IsArchived = true;
        await t.Db.SaveChangesAsync();
        var after = Assert.Single(await svc.ListAsync());
        Assert.False(Assert.Single(after.Lines, l => l.Kind == "expense").IsAvailable);
        Assert.Equal(1, after.UnavailableCount);
    }

    [Fact]
    public async Task Create_Validates_AndArchiveHides()
    {
        using var t = new TestDb();
        var svc = new BundleService(t.Db);
        var line = new[] { new BundleLineInput("expense", Guid.NewGuid(), 5m, null, "x") };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync("  ", line));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync("Empty", Array.Empty<BundleLineInput>()));

        var b = await svc.CreateAsync("Costco run", line);
        Assert.Single(await svc.ListAsync());

        await svc.ArchiveAsync(b.Id);
        Assert.Empty(await svc.ListAsync());
    }
}

// Weigh-ins: one row per day (last write wins), windowed oldest-first trend.
public class BalanceSnapshotTests
{
    [Fact]
    public async Task Record_UpsertsByDay_AndTrendIsWindowedOldestFirst()
    {
        using var t = new TestDb();
        var svc = new BalanceSnapshotService(t.Db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        await svc.RecordAsync(100m, today.AddDays(-200));   // ancient — outside every window
        await svc.RecordAsync(110m, today.AddDays(-10));
        await svc.RecordAsync(120m, today);
        await svc.RecordAsync(125m, today);                 // same-day correction wins

        Assert.Equal(3, await t.Db.BalanceSnapshots.CountAsync());

        var trend = await svc.GetTrendAsync(90);
        Assert.Equal(2, trend.Count);                       // the ancient row is out of window
        Assert.Equal(today.AddDays(-10), trend[0].Date);    // oldest first
        Assert.Equal(125m, trend[^1].Total);                // last write won
    }
}
