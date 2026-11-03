using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Data;

public class AppDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<FamilyTransaction> FamilyTransactions => Set<FamilyTransaction>();
    public DbSet<FamilyBudget> FamilyBudgets => Set<FamilyBudget>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<FamilyGroup>()
            .HasIndex(g => g.InviteCode)
            .IsUnique();

        builder.Entity<FamilyMember>()
            .HasIndex(m => m.UserId)
            .IsUnique();

        builder.Entity<FamilyMember>()
            .HasOne(m => m.FamilyGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.FamilyGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Wallet>()
            .HasIndex(w => w.UserId);

        builder.Entity<FamilyTransaction>()
            .HasIndex(t => new { t.UserId, t.EventId })
            .IsUnique();

        builder.Entity<FamilyTransaction>()
            .HasOne<FamilyGroup>()
            .WithMany()
            .HasForeignKey(t => t.FamilyGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<FamilyTransaction>()
            .HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<FamilyTransaction>()
            .HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(t => t.ToWalletId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<FamilyBudget>()
            .HasIndex(b => new { b.FamilyGroupId, b.Year, b.Month })
            .IsUnique();

        builder.Entity<FamilyBudget>()
            .HasOne<FamilyGroup>()
            .WithMany()
            .HasForeignKey(b => b.FamilyGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DeviceToken>()
            .HasIndex(d => new { d.UserId, d.Token })
            .IsUnique();
    }
}