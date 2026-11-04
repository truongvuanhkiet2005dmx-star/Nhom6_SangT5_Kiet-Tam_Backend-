using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartMoney.Api.Controllers;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

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

    private static ControllerContext CreateControllerContext(string userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
            "Test");
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static TransactionsController CreateTransactionsController(AppDbContext db, string userId)
    {
        var push = new PushNotificationService(null);
        var alerts = new BudgetAlertService(db, push, NullLogger<BudgetAlertService>.Instance);
        return new TransactionsController(
            db,
            alerts,
            NullLogger<TransactionsController>.Instance)
        {
            ControllerContext = CreateControllerContext(userId)
        };
    }

    private static WalletsController CreateWalletsController(AppDbContext db, string userId)
    {
        return new WalletsController(db, new WalletBalanceService(db))
        {
            ControllerContext = CreateControllerContext(userId)
        };
    }

    [Fact]
    public async Task BalanceIncludesAllTransactionKinds()
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

            var balance = await new WalletBalanceService(db).CalculateCurrentBalanceAsync(wallet, "u1");

            Assert.Equal(1500, balance);
        }
    }

    [Fact]
    public async Task TransactionSameEventIdSameDataReturnsSameIdAndOneRow()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var wallet = new Wallet { UserId = "u1", Name = "Tiền mặt", Type = "cash", InitialBalance = 1000 };
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();

            var controller = CreateTransactionsController(db, "u1");
            var request = new TransactionRequest
            {
                EventId = "event-1",
                Amount = 100,
                Type = "expense",
                WalletId = wallet.Id,
                CategoryName = "Ăn uống"
            };

            var first = await controller.Create(request);
            var second = await controller.Create(request);
            var firstResponse = Assert.IsType<OkObjectResult>(first).Value as TransactionResponse;
            var secondResponse = Assert.IsType<OkObjectResult>(second).Value as TransactionResponse;

            Assert.NotNull(firstResponse);
            Assert.NotNull(secondResponse);
            Assert.Equal(firstResponse.Id, secondResponse.Id);
            Assert.Equal(1, await db.FamilyTransactions.CountAsync(t => t.UserId == "u1" && t.EventId == "event-1"));
        }
    }

    [Fact]
    public async Task TransactionSameEventIdDifferentAmountReturnsConflict()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var wallet = new Wallet { UserId = "u1", Name = "Tiền mặt", Type = "cash" };
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();

            var controller = CreateTransactionsController(db, "u1");
            var first = await controller.Create(new TransactionRequest
            {
                EventId = "event-2",
                Amount = 100,
                Type = "expense",
                WalletId = wallet.Id
            });
            var second = await controller.Create(new TransactionRequest
            {
                EventId = "event-2",
                Amount = 200,
                Type = "expense",
                WalletId = wallet.Id
            });

            Assert.IsType<OkObjectResult>(first);
            Assert.IsType<ConflictObjectResult>(second);
            Assert.Equal(1, await db.FamilyTransactions.CountAsync(t => t.EventId == "event-2"));
        }
    }

    [Fact]
    public async Task WalletSameClientIdSameDataReturnsSameIdAndOneWallet()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var controller = CreateWalletsController(db, "u1");
            var request = new WalletRequest
            {
                Name = "Ví chính",
                Type = "cash",
                InitialBalance = 1000,
                ClientId = "wallet-client-id"
            };

            var first = await controller.Create(request);
            var second = await controller.Create(request);
            var firstResponse = Assert.IsType<OkObjectResult>(first).Value as WalletResponse;
            var secondResponse = Assert.IsType<OkObjectResult>(second).Value as WalletResponse;

            Assert.NotNull(firstResponse);
            Assert.NotNull(secondResponse);
            Assert.Equal(firstResponse.Id, secondResponse.Id);
            Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == "u1" && w.ClientId == "wallet-client-id"));
        }
    }

    [Fact]
    public async Task WalletSameClientIdDifferentDataReturnsConflict()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var controller = CreateWalletsController(db, "u1");
            var first = await controller.Create(new WalletRequest
            {
                Name = "Ví chính",
                Type = "cash",
                InitialBalance = 1000,
                ClientId = "same-client"
            });
            var second = await controller.Create(new WalletRequest
            {
                Name = "Ví khác",
                Type = "cash",
                InitialBalance = 1000,
                ClientId = "same-client"
            });

            Assert.IsType<OkObjectResult>(first);
            Assert.IsType<ConflictObjectResult>(second);
            Assert.Equal(1, await db.Wallets.CountAsync(w => w.ClientId == "same-client"));
        }
    }

    [Fact]
    public async Task DeleteWalletReferencedByTransactionReturnsConflictAndKeepsWallet()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var wallet = new Wallet { UserId = "u1", Name = "Ví chính", Type = "cash" };
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
            db.FamilyTransactions.Add(new FamilyTransaction
            {
                EventId = "delete-wallet-event",
                UserId = "u1",
                WalletId = wallet.Id,
                Type = "expense",
                Amount = 100
            });
            await db.SaveChangesAsync();

            var controller = CreateWalletsController(db, "u1");
            var result = await controller.Delete(wallet.Id);

            Assert.IsType<ConflictObjectResult>(result);
            Assert.True(await db.Wallets.AnyAsync(w => w.Id == wallet.Id));
        }
    }

    [Fact]
    public async Task AdjustmentWithoutDirectionReturnsBadRequest()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var wallet = new Wallet { UserId = "u1", Name = "Ví chính", Type = "cash" };
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();

            var controller = CreateTransactionsController(db, "u1");
            var result = await controller.Create(new TransactionRequest
            {
                EventId = "adjustment-event",
                Amount = 100,
                Type = "adjustment",
                WalletId = wallet.Id
            });

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(0, await db.FamilyTransactions.CountAsync());
        }
    }
}

public class AccountCleanupTests
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

    private static UserManager<ApplicationUser> CreateUserManager(AppDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);
        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    [Fact]
    public async Task CleanupDeletesOnlyUserADataAndKeepsUserBWalletAndTransaction()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var userA = new ApplicationUser
            {
                Id = "a",
                UserName = "a@example.com",
                NormalizedUserName = "A@EXAMPLE.COM",
                Email = "a@example.com",
                NormalizedEmail = "A@EXAMPLE.COM",
                EmailConfirmed = true
            };
            db.Users.Add(userA);

            var group = new FamilyGroup
            {
                Name = "Nhóm A",
                OwnerUserId = "a",
                InviteCode = "ABC123"
            };
            db.FamilyGroups.Add(group);
            await db.SaveChangesAsync();

            db.FamilyMembers.Add(new FamilyMember
            {
                FamilyGroupId = group.Id,
                UserId = "a",
                DisplayName = "A",
                AccessRole = "Owner"
            });
            db.FamilyBudgets.Add(new FamilyBudget
            {
                FamilyGroupId = group.Id,
                Year = 2026,
                Month = 11,
                MonthlyLimit = 100000
            });

            var walletA = new Wallet { UserId = "a", Name = "Ví A", InitialBalance = 100 };
            var walletB = new Wallet { UserId = "b", Name = "Ví B", InitialBalance = 200 };
            db.Wallets.AddRange(walletA, walletB);
            await db.SaveChangesAsync();

            db.FamilyTransactions.AddRange(
                new FamilyTransaction
                {
                    EventId = "a-event",
                    UserId = "a",
                    FamilyGroupId = group.Id,
                    WalletId = walletA.Id,
                    Type = "expense",
                    Amount = 50
                },
                new FamilyTransaction
                {
                    EventId = "b-event",
                    UserId = "b",
                    FamilyGroupId = group.Id,
                    WalletId = walletB.Id,
                    Type = "income",
                    Amount = 75
                });
            db.EmailOtpChallenges.Add(new EmailOtpChallenge
            {
                Email = "a@example.com",
                CodeHash = "hash",
                CodeSalt = "salt",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            });
            await db.SaveChangesAsync();

            var cleanup = new AccountCleanupService(db, CreateUserManager(db));
            await cleanup.CleanupAsync(userA);

            Assert.False(await db.Wallets.AnyAsync(w => w.UserId == "a"));
            Assert.False(await db.FamilyTransactions.AnyAsync(t => t.UserId == "a"));
            Assert.True(await db.Wallets.AnyAsync(w => w.UserId == "b"));
            Assert.True(await db.FamilyTransactions.AnyAsync(t => t.UserId == "b"));
            Assert.False(await db.FamilyGroups.AnyAsync(g => g.Id == group.Id));
        }
    }

    [Fact]
    public async Task OwnedFamilyWithOtherMemberIsDetected()
    {
        var (db, connection) = await CreateDbAsync();
        await using (connection)
        await using (db)
        {
            var group = new FamilyGroup
            {
                Name = "Nhóm chung",
                OwnerUserId = "owner",
                InviteCode = "OWNER1"
            };
            db.FamilyGroups.Add(group);
            await db.SaveChangesAsync();

            db.FamilyMembers.AddRange(
                new FamilyMember
                {
                    FamilyGroupId = group.Id,
                    UserId = "owner",
                    AccessRole = "Owner"
                },
                new FamilyMember
                {
                    FamilyGroupId = group.Id,
                    UserId = "member-b",
                    AccessRole = "Member"
                });
            await db.SaveChangesAsync();

            var cleanup = new AccountCleanupService(db, CreateUserManager(db));
            Assert.True(await cleanup.HasOwnedFamilyWithOtherMembersAsync("owner"));
        }
    }
}
