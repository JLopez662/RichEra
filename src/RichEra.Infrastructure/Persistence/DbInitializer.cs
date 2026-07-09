using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;

namespace RichEra.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task MigrateAndSeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Categories.AnyAsync(ct))
        {
            db.Categories.AddRange(DefaultCategories());
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await BackfillCategoryBucketsAsync(db, ct);
        }
    }

    // Databases created before macro buckets existed have all-null category buckets. Fill in
    // the ones we recognize (the seeded names) and leave everything else for the user to sort.
    // Idempotent: only touches Expense categories that are still null.
    private static async Task BackfillCategoryBucketsAsync(AppDbContext db, CancellationToken ct)
    {
        var unsorted = await db.Categories
            .Where(c => c.Kind == CategoryKind.Expense && c.Bucket == null)
            .ToListAsync(ct);
        if (unsorted.Count == 0) return;

        var changed = false;
        foreach (var cat in unsorted)
        {
            if (!SeedBuckets.TryGetValue(cat.Name, out var bucket)) continue;
            cat.Bucket = bucket;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    // Bucket defaults for the seeded names — mirrors DefaultCategories below.
    private static readonly Dictionary<string, MacroBucket> SeedBuckets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Groceries"] = MacroBucket.Needs,
        ["Rent"] = MacroBucket.Needs,
        ["Utilities"] = MacroBucket.Needs,
        ["Transport"] = MacroBucket.Needs,
        ["Healthcare"] = MacroBucket.Needs,
        ["Dining"] = MacroBucket.Wants,
        ["Subscriptions"] = MacroBucket.Wants,
        ["Entertainment"] = MacroBucket.Wants,
        // "Other" stays unassigned on purpose — it means "not sorted yet".
    };

    private static IEnumerable<Category> DefaultCategories() => new[]
    {
        new Category { Name = "Salary",       Kind = CategoryKind.Income,  Icon = "briefcase",  ColorHex = "#2ECC71" },
        new Category { Name = "Side Income",  Kind = CategoryKind.Income,  Icon = "trending-up",ColorHex = "#27AE60" },
        new Category { Name = "Interest",     Kind = CategoryKind.Income,  Icon = "percent",    ColorHex = "#16A085" },

        new Category { Name = "Groceries",    Kind = CategoryKind.Expense, Icon = "shopping-cart", ColorHex = "#E67E22", Bucket = MacroBucket.Needs },
        new Category { Name = "Rent",         Kind = CategoryKind.Expense, Icon = "home",          ColorHex = "#E74C3C", Bucket = MacroBucket.Needs },
        new Category { Name = "Utilities",    Kind = CategoryKind.Expense, Icon = "zap",           ColorHex = "#F39C12", Bucket = MacroBucket.Needs },
        new Category { Name = "Transport",    Kind = CategoryKind.Expense, Icon = "car",           ColorHex = "#3498DB", Bucket = MacroBucket.Needs },
        new Category { Name = "Dining",       Kind = CategoryKind.Expense, Icon = "coffee",        ColorHex = "#D35400", Bucket = MacroBucket.Wants },
        new Category { Name = "Subscriptions",Kind = CategoryKind.Expense, Icon = "repeat",        ColorHex = "#9B59B6", Bucket = MacroBucket.Wants },
        new Category { Name = "Healthcare",   Kind = CategoryKind.Expense, Icon = "heart",         ColorHex = "#E91E63", Bucket = MacroBucket.Needs },
        new Category { Name = "Entertainment",Kind = CategoryKind.Expense, Icon = "film",          ColorHex = "#8E44AD", Bucket = MacroBucket.Wants },
        new Category { Name = "Other",        Kind = CategoryKind.Expense, Icon = "more-horizontal",ColorHex = "#7F8C8D" },
    };
}
