using AssetMap.Entities;
using AssetMap.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace AssetMap.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User>              Users              => Set<User>();
    public DbSet<Account>           Accounts           => Set<Account>();
    public DbSet<Asset>             Assets             => Set<Asset>();
    public DbSet<Holding>           Holdings           => Set<Holding>();
    public DbSet<Transaction>       Transactions       => Set<Transaction>();
    public DbSet<PriceSnapshot>     PriceSnapshots     => Set<PriceSnapshot>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();
    public DbSet<ImportBatch>       ImportBatches      => Set<ImportBatch>();
    public DbSet<SyncLog>           SyncLogs           => Set<SyncLog>();
    public DbSet<WatchedWallet>     WatchedWallets     => Set<WatchedWallet>();
    public DbSet<AssetPriceFeed>    AssetPriceFeeds    => Set<AssetPriceFeed>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // ── User ─────────────────────────────────────────────────
        mb.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
        });

        // ── Account ───────────────────────────────────────────────
        mb.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasOne(a => a.User)
             .WithMany(u => u.Accounts)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Asset ─────────────────────────────────────────────────
        mb.Entity<Asset>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Symbol);
        });

        // ── Holding ───────────────────────────────────────────────
        mb.Entity<Holding>(e =>
        {
            e.HasKey(h => h.Id);
            e.HasOne(h => h.Account)
             .WithMany(a => a.Holdings)
             .HasForeignKey(h => h.AccountId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Asset)
             .WithMany(a => a.Holdings)
             .HasForeignKey(h => h.AssetId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Transaction ───────────────────────────────────────────
        mb.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasOne(t => t.Account)
             .WithMany(a => a.Transactions)
             .HasForeignKey(t => t.AccountId)
             .OnDelete(DeleteBehavior.Cascade);

            // Hlavní asset (primární FK → Asset.Transactions)
            e.HasOne(t => t.Asset)
             .WithMany(a => a.Transactions)
             .HasForeignKey(t => t.AssetId)
             .OnDelete(DeleteBehavior.Restrict);

            // Vedlejší FK na Asset — bez zpětné navigace
            e.HasOne(t => t.CounterAsset)
             .WithMany()
             .HasForeignKey(t => t.CounterAssetId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.FeeAsset)
             .WithMany()
             .HasForeignKey(t => t.FeeAssetId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.RelatedAsset)
             .WithMany()
             .HasForeignKey(t => t.RelatedAssetId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.FromAccount)
             .WithMany()
             .HasForeignKey(t => t.FromAccountId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.ToAccount)
             .WithMany()
             .HasForeignKey(t => t.ToAccountId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(t => t.ImportBatch)
             .WithMany(b => b.Transactions)
             .HasForeignKey(t => t.ImportBatchId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── PriceSnapshot ─────────────────────────────────────────
        mb.Entity<PriceSnapshot>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.AssetId, p.Timestamp });
            e.HasOne(p => p.Asset)
             .WithMany(a => a.PriceSnapshots)
             .HasForeignKey(p => p.AssetId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PortfolioSnapshot ─────────────────────────────────────
        mb.Entity<PortfolioSnapshot>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.AccountId, p.Timestamp });
            e.HasOne(p => p.User)
             .WithMany()
             .HasForeignKey(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Account)
             .WithMany()
             .HasForeignKey(p => p.AccountId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WatchedWallet ─────────────────────────────────────────
        mb.Entity<WatchedWallet>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasOne(w => w.Account)
             .WithMany(a => a.WatchedWallets)
             .HasForeignKey(w => w.AccountId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ImportBatch ───────────────────────────────────────────
        mb.Entity<ImportBatch>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasOne(b => b.Account)
             .WithMany(a => a.ImportBatches)
             .HasForeignKey(b => b.AccountId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SyncLog ───────────────────────────────────────────────
        mb.Entity<SyncLog>(e =>
        {
            e.HasKey(s => s.Id);
        });

        // ── AssetPriceFeed ────────────────────────────────────────
        mb.Entity<AssetPriceFeed>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.Asset)
             .WithMany(a => a.PriceFeeds)
             .HasForeignKey(f => f.AssetId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ImportBatch: UserId → User ────────────────────────────
        mb.Entity<ImportBatch>(e =>
        {
            e.HasOne(b => b.User)
             .WithMany(u => u.ImportBatches)
             .HasForeignKey(b => b.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed: výchozí uživatel (single-user self-hosted) ──────
        mb.Entity<User>().HasData(new User
        {
            Id           = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Username     = "admin",
            PasswordHash = "", // nastavit při prvním spuštění
            Role         = UserRole.Admin,
            CreatedAt    = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
