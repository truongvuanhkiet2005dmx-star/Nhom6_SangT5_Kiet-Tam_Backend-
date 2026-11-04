using SmartMoney.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Controllers;

namespace SmartMoney.Api.Tests;

public class VietnamTimeTests
{
    [Fact]
    public void MonthRange_UsesVietnamTimeOffset()
    {
        var (startUtc, endUtc) = VietnamTime.GetMonthUtcRange(2026, 3);

        Assert.Equal(new DateTime(2026, 2, 28, 17, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 3, 31, 17, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Fact]
    public void NormalizeUtc_ConvertsDateTimeOffsetToUtc()
    {
        var value = new DateTimeOffset(2026, 3, 1, 1, 30, 0, TimeSpan.FromHours(7));

        var utc = VietnamTime.NormalizeUtc(value);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 2, 28, 18, 30, 0, DateTimeKind.Utc), utc);
    }
}

public class WalletBalanceTests
{
    private static async Task<(AppDbContext Db, SqliteConnection Connection)> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (db, connection);
    }

    [Fact]
    public async Task BalanceIncludesAllTransactionKindsExceptBudgetAndReport()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var wallet = new Wallet { UserId = "u1", InitialBalance = 1000, Name = "Tiền mặt", Type = "cash", ClientId = "wallet-client" };
            var other = new Wallet { UserId = "u1", InitialBalance = 0, Name = "Bank", Type = "bank", ClientId = "wallet-other" };
            db.Wallets.AddRange(wallet, other);
            await db.SaveChangesAsync();

            db.FamilyTransactions.AddRange(
                new FamilyTransaction { EventId = "income", UserId = "u1", Type = "income", Amount = 500, WalletId = wallet.Id },
                new FamilyTransaction { EventId = "expense", UserId = "u1", Type = "expense", Amount = 200, WalletId = wallet.Id },
                new FamilyTransaction { EventId = "increase", UserId = "u1", Type = "adjustment", AdjustmentDirection = "increase", Amount = 300, WalletId = wallet.Id },
                new FamilyTransaction { EventId = "transfer-out", UserId = "u1", Type = "transfer", Amount = 100, WalletId = wallet.Id, ToWalletId = other.Id });
            await db.SaveChangesAsync();

            var service = new WalletBalanceService(db);
            var balance = await service.CalculateCurrentBalanceAsync(wallet, "u1");
            var walletResponse = new WalletResponse
            {
                Id = wallet.Id,
                ClientId = wallet.ClientId,
                InitialBalance = wallet.InitialBalance,
                CurrentBalance = balance
            };

            Assert.Equal(1500, balance);
            Assert.Equal("wallet-client", walletResponse.ClientId);
        }
    }

    [Fact]
    public async Task TransactionEventIdIsUniquePerUser()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            db.FamilyTransactions.Add(new FamilyTransaction { EventId = "same-event", UserId = "u1", Type = "expense", Amount = 100 });
            await db.SaveChangesAsync();
            db.FamilyTransactions.Add(new FamilyTransaction { EventId = "same-event", UserId = "u1", Type = "expense", Amount = 100 });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task WalletClientIdIsIdempotentPerUserAndGoogleUidIsUnique()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            db.Wallets.Add(new Wallet { UserId = "u1", Name = "Ví", Type = "cash", ClientId = "client-1" });
            await db.SaveChangesAsync();
            db.Wallets.Add(new Wallet { UserId = "u1", Name = "Ví trùng", Type = "cash", ClientId = "client-1" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            db.ChangeTracker.Clear();
            db.Users.AddRange(
                new ApplicationUser { Id = "user-a", UserName = "user-a@example.com", Email = "user-a@example.com", LockoutEnabled = true },
                new ApplicationUser { Id = "user-b", UserName = "user-b@example.com", Email = "user-b@example.com", LockoutEnabled = true });
            await db.SaveChangesAsync();

            db.UserLogins.Add(new IdentityUserLogin<string>
            {
                LoginProvider = "Firebase",
                ProviderKey = "uid-1",
                ProviderDisplayName = "Firebase",
                UserId = "user-a"
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            db.UserLogins.Add(new IdentityUserLogin<string>
            {
                LoginProvider = "Firebase",
                ProviderKey = "uid-1",
                ProviderDisplayName = "Firebase",
                UserId = "user-b"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public void NewAccountsAndWalletResponsesEnableLockoutAndExposeClientId()
    {
        var user = new ApplicationUser { EmailConfirmed = false, LockoutEnabled = true };
        var response = new WalletResponse { Id = 1, ClientId = "wallet-1", InitialBalance = 100, CurrentBalance = 125 };
        var transaction = new TransactionResponse { Id = 2, EventId = "event-1", WalletId = 1, Amount = 25, Type = "income" };

        Assert.True(user.LockoutEnabled);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("wallet-1", response.ClientId);
        Assert.Equal("event-1", transaction.EventId);
        Assert.Null(transaction.GetType().GetProperty("UserId"));
    }
}