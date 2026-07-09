using Microsoft.EntityFrameworkCore;
using RichEra.Domain.Entities;

namespace RichEra.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsContribution> SavingsContributions => Set<SavingsContribution>();
    public DbSet<ItemImage> ItemImages => Set<ItemImage>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Bundle> Bundles => Set<Bundle>();
    public DbSet<BundleLine> BundleLines => Set<BundleLine>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.OpeningBalance).HasPrecision(18, 4);
            e.Property(x => x.CurrentBalance).HasPrecision(18, 4);
            e.Property(x => x.ExternalAccountId).HasMaxLength(64);
            e.Property(x => x.BankId).HasMaxLength(32);
            e.HasIndex(x => x.IsArchived);
            e.HasIndex(x => x.ExternalAccountId); // statement-import account matching
        });

        b.Entity<Category>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(80);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.ColorHex).HasMaxLength(9);
            e.HasIndex(x => new { x.Kind, x.IsArchived });
        });

        b.Entity<Transaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.ImportSource).HasMaxLength(20);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.TransferGroupId);
            e.HasIndex(x => x.ItemId);
            e.HasIndex(x => x.SavingsSourceTransactionId);
            e.HasIndex(x => x.SavingsGoalId);
            // FITID-based dedupe key for statement imports. Filtered so manual/recurrence
            // rows (null ExternalId) are exempt from the uniqueness constraint.
            e.HasIndex(x => new { x.AccountId, x.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");
        });

        b.Entity<Item>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Counterparty).HasMaxLength(120);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.Property(x => x.UnitName).HasMaxLength(40);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.AprPct).HasPrecision(8, 4);
            e.Property(x => x.PrincipalBalance).HasPrecision(18, 4);
            e.Property(x => x.ExpectedReturnPct).HasPrecision(8, 4);
            e.Property(x => x.FeesPerUnit).HasPrecision(18, 4);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Direction, x.IsArchived });
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(120);
            e.Property(x => x.Value).IsRequired().HasMaxLength(2000);
        });

        b.Entity<Bundle>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.HasMany(x => x.Lines).WithOne(x => x.Bundle).HasForeignKey(x => x.BundleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.IsArchived);
        });

        b.Entity<BundleLine>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).IsRequired().HasMaxLength(16);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.Property(x => x.TitleSnapshot).IsRequired().HasMaxLength(140);
            e.HasIndex(x => x.BundleId);
        });

        b.Entity<BalanceSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TotalBalance).HasPrecision(18, 4);
            e.HasIndex(x => x.Date).IsUnique();   // one weigh-in per day
        });

        b.Entity<SavingsGoal>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.TargetAmount).HasPrecision(18, 4);
            e.HasOne(x => x.LinkedAccount).WithMany().HasForeignKey(x => x.LinkedAccountId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.IsArchived);
        });

        b.Entity<SavingsContribution>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.HasOne(x => x.Goal).WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.GoalId);
        });

        b.Entity<ItemImage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).IsRequired().HasMaxLength(16);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(64);
            // One image per item; the lookup is always by (Kind, OwnerId).
            e.HasIndex(x => new { x.Kind, x.OwnerId }).IsUnique();
        });
    }
}
