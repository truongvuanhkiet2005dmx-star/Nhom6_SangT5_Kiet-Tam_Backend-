using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Services;

public class AccountCleanupService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public AccountCleanupService(
        AppDbContext db,
        UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<bool> HasOwnedFamilyWithOtherMembersAsync(string userId)
    {
        return await _db.FamilyGroups.AnyAsync(g =>
            g.OwnerUserId == userId &&
            _db.FamilyMembers.Any(m =>
                m.FamilyGroupId == g.Id && m.UserId != userId));
    }

    public async Task CleanupAsync(ApplicationUser user)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            var userId = user.Id;
            var email = user.Email?.Trim().ToLowerInvariant() ?? "";

            var deviceTokens = await _db.DeviceTokens
                .Where(d => d.UserId == userId)
                .ToListAsync();
            _db.DeviceTokens.RemoveRange(deviceTokens);

            var transactions = await _db.FamilyTransactions
                .Where(t => t.UserId == userId)
                .ToListAsync();
            _db.FamilyTransactions.RemoveRange(transactions);

            var wallets = await _db.Wallets
                .Where(w => w.UserId == userId)
                .ToListAsync();
            _db.Wallets.RemoveRange(wallets);

            var memberships = await _db.FamilyMembers
                .Where(m => m.UserId == userId)
                .ToListAsync();
            _db.FamilyMembers.RemoveRange(memberships);
            await _db.SaveChangesAsync();

            var ownedGroups = await _db.FamilyGroups
                .Where(g => g.OwnerUserId == userId)
                .ToListAsync();

            foreach (var group in ownedGroups)
            {
                var hasOtherMember = await _db.FamilyMembers.AnyAsync(m =>
                    m.FamilyGroupId == group.Id && m.UserId != userId);
                if (hasOtherMember)
                    continue;

                var remainingTransactions = await _db.FamilyTransactions
                    .Where(t => t.FamilyGroupId == group.Id)
                    .ToListAsync();
                foreach (var transactionRecord in remainingTransactions)
                    transactionRecord.FamilyGroupId = null;

                var budgets = await _db.FamilyBudgets
                    .Where(b => b.FamilyGroupId == group.Id)
                    .ToListAsync();
                _db.FamilyBudgets.RemoveRange(budgets);
                _db.FamilyGroups.Remove(group);
            }

            if (!string.IsNullOrEmpty(email))
            {
                var otpChallenges = await _db.EmailOtpChallenges
                    .Where(c => c.Email == email)
                    .ToListAsync();
                _db.EmailOtpChallenges.RemoveRange(otpChallenges);
            }

            await _db.SaveChangesAsync();

            var deleteResult = await _users.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                throw new InvalidOperationException(
                    string.Join(" ", deleteResult.Errors.Select(e => e.Description)));
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
