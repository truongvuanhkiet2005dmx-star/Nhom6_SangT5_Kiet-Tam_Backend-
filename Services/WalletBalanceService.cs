using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Services;

public class WalletBalanceService
{
    private readonly AppDbContext _db;

    public WalletBalanceService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<long> CalculateCurrentBalanceAsync(
        Wallet wallet,
        string userId)
    {
        var balances = await CalculateCurrentBalancesAsync([wallet], userId);
        return balances.GetValueOrDefault(wallet.Id, wallet.InitialBalance);
    }

    public async Task<Dictionary<int, long>> CalculateCurrentBalancesAsync(
        IReadOnlyCollection<Wallet> wallets,
        string userId)
    {
        if (wallets.Count == 0)
            return new Dictionary<int, long>();

        var walletIds = wallets.Select(w => w.Id).ToList();
        var result = new Dictionary<int, long>();
        foreach (var wallet in wallets)
            result[wallet.Id] = wallet.InitialBalance;

        await ApplySourceTotalsAsync(
            result, walletIds, userId, "income", 1);
        await ApplySourceTotalsAsync(
            result, walletIds, userId, "expense", -1);
        await ApplySourceTotalsAsync(
            result, walletIds, userId, "transfer", -1);
        await ApplyDestinationTotalsAsync(
            result, walletIds, userId, "transfer", 1);
        await ApplyAdjustmentTotalsAsync(
            result, walletIds, userId, "increase", 1);
        await ApplyAdjustmentTotalsAsync(
            result, walletIds, userId, "decrease", -1);

        return result;
    }

    private async Task ApplySourceTotalsAsync(
        Dictionary<int, long> balances,
        IReadOnlyCollection<int> walletIds,
        string userId,
        string type,
        int sign)
    {
        var totals = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.Type == type &&
                        t.WalletId.HasValue &&
                        walletIds.Contains(t.WalletId.Value))
            .GroupBy(t => t.WalletId!.Value)
            .Select(g => new { WalletId = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToListAsync();

        foreach (var total in totals)
            balances[total.WalletId] += sign * total.Amount;
    }

    private async Task ApplyDestinationTotalsAsync(
        Dictionary<int, long> balances,
        IReadOnlyCollection<int> walletIds,
        string userId,
        string type,
        int sign)
    {
        var totals = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.Type == type &&
                        t.ToWalletId.HasValue &&
                        walletIds.Contains(t.ToWalletId.Value))
            .GroupBy(t => t.ToWalletId!.Value)
            .Select(g => new { WalletId = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToListAsync();

        foreach (var total in totals)
            balances[total.WalletId] += sign * total.Amount;
    }

    private async Task ApplyAdjustmentTotalsAsync(
        Dictionary<int, long> balances,
        IReadOnlyCollection<int> walletIds,
        string userId,
        string direction,
        int sign)
    {
        var totals = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        t.Type == "adjustment" &&
                        t.AdjustmentDirection == direction &&
                        t.WalletId.HasValue &&
                        walletIds.Contains(t.WalletId.Value))
            .GroupBy(t => t.WalletId!.Value)
            .Select(g => new { WalletId = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToListAsync();

        foreach (var total in totals)
            balances[total.WalletId] += sign * total.Amount;
    }
}
