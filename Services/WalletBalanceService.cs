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
        var transactions = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        (t.WalletId == wallet.Id || t.ToWalletId == wallet.Id))
            .ToListAsync();

        return CalculateCurrentBalance(wallet, transactions);
    }

    public async Task<Dictionary<int, long>> CalculateCurrentBalancesAsync(
        IReadOnlyCollection<Wallet> wallets,
        string userId)
    {
        if (wallets.Count == 0)
            return new Dictionary<int, long>();

        var walletIds = wallets.Select(w => w.Id).ToList();
        var transactions = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        (t.WalletId.HasValue && walletIds.Contains(t.WalletId.Value) ||
                         t.ToWalletId.HasValue && walletIds.Contains(t.ToWalletId.Value)))
            .ToListAsync();

        var result = new Dictionary<int, long>();
        foreach (var wallet in wallets)
        {
            result[wallet.Id] = CalculateCurrentBalance(wallet, transactions);
        }
        return result;
    }

    private static long CalculateCurrentBalance(
        Wallet wallet,
        IReadOnlyCollection<FamilyTransaction> transactions)
    {
        return wallet.InitialBalance
            + transactions.Where(t => t.Type == "income" && t.WalletId == wallet.Id).Sum(t => t.Amount)
            - transactions.Where(t => t.Type == "expense" && t.WalletId == wallet.Id).Sum(t => t.Amount)
            - transactions.Where(t => t.Type == "transfer" && t.WalletId == wallet.Id).Sum(t => t.Amount)
            + transactions.Where(t => t.Type == "transfer" && t.ToWalletId == wallet.Id).Sum(t => t.Amount)
            + transactions.Where(t => t.Type == "adjustment" &&
                                   t.WalletId == wallet.Id &&
                                   t.AdjustmentDirection == "increase").Sum(t => t.Amount)
            - transactions.Where(t => t.Type == "adjustment" &&
                                   t.WalletId == wallet.Id &&
                                   t.AdjustmentDirection == "decrease").Sum(t => t.Amount);
    }
}
