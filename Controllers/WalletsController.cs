using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
public class WalletsController : ControllerBase
{
    private const long MaxMoneyValue = 1_000_000_000_000L;
    private readonly AppDbContext _db;

    public WalletsController(AppDbContext db)
    {
        _db = db;
    }

    // Lấy danh sách ví kèm số dư hiện tại
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var wallets = await _db.Wallets
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.Name)
            .ToListAsync();

        var walletIds = wallets.Select(w => w.Id).ToList();
        var transactions = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        (t.WalletId.HasValue && walletIds.Contains(t.WalletId.Value) ||
                         t.ToWalletId.HasValue && walletIds.Contains(t.ToWalletId.Value)))
            .Select(t => new { t.Amount, t.Type, t.WalletId, t.ToWalletId, t.AdjustmentDirection })
            .ToListAsync();

        var result = new List<object>();

        foreach (var wallet in wallets)
        {
            var income = transactions
                .Where(t => t.Type == "income" && t.WalletId == wallet.Id)
                .Sum(t => t.Amount);

            var expense = transactions
                .Where(t => t.Type == "expense" && t.WalletId == wallet.Id)
                .Sum(t => t.Amount);

            var transferOut = transactions
                .Where(t => t.Type == "transfer" && t.WalletId == wallet.Id)
                .Sum(t => t.Amount);

            var transferIn = transactions
                .Where(t => t.Type == "transfer" && t.ToWalletId == wallet.Id)
                .Sum(t => t.Amount);

            var adjustmentIncrease = transactions
                .Where(t => t.Type == "adjustment" &&
                            t.WalletId == wallet.Id &&
                            t.AdjustmentDirection == "increase")
                .Sum(t => t.Amount);

            var adjustmentDecrease = transactions
                .Where(t => t.Type == "adjustment" &&
                            t.WalletId == wallet.Id &&
                            t.AdjustmentDirection == "decrease")
                .Sum(t => t.Amount);

            result.Add(new
            {
                wallet.Id,
                wallet.Name,
                wallet.Type,
                wallet.InitialBalance,
                CurrentBalance = wallet.InitialBalance + income - expense - transferOut + transferIn + adjustmentIncrease - adjustmentDecrease,
                wallet.CreatedAtUtc
            });
        }

        return Ok(result);
    }

    // Lấy một ví
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOne(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
            return NotFound("Không tìm thấy ví.");

        var transactions = await _db.FamilyTransactions
            .Where(t => t.UserId == userId &&
                        (t.WalletId == wallet.Id || t.ToWalletId == wallet.Id))
            .Select(t => new { t.Amount, t.Type, t.WalletId, t.ToWalletId, t.AdjustmentDirection })
            .ToListAsync();

        var income = transactions
            .Where(t => t.Type == "income" && t.WalletId == wallet.Id)
            .Sum(t => t.Amount);

        var expense = transactions
            .Where(t => t.Type == "expense" && t.WalletId == wallet.Id)
            .Sum(t => t.Amount);

        var transferOut = transactions
            .Where(t => t.Type == "transfer" && t.WalletId == wallet.Id)
            .Sum(t => t.Amount);

        var transferIn = transactions
            .Where(t => t.Type == "transfer" && t.ToWalletId == wallet.Id)
            .Sum(t => t.Amount);

        var adjustmentIncrease = transactions
            .Where(t => t.Type == "adjustment" &&
                        t.WalletId == wallet.Id &&
                        t.AdjustmentDirection == "increase")
            .Sum(t => t.Amount);

        var adjustmentDecrease = transactions
            .Where(t => t.Type == "adjustment" &&
                        t.WalletId == wallet.Id &&
                        t.AdjustmentDirection == "decrease")
            .Sum(t => t.Amount);

        return Ok(new
        {
            wallet.Id,
            wallet.Name,
            wallet.Type,
            wallet.InitialBalance,
            CurrentBalance = wallet.InitialBalance + income - expense - transferOut + transferIn + adjustmentIncrease - adjustmentDecrease,
            wallet.CreatedAtUtc
        });
    }

    // Tạo ví
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WalletRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tên ví không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Type))
            return BadRequest("Type là bắt buộc khi tạo ví.");

        var type = request.Type.Trim().ToLowerInvariant();
        if (type != "cash" && type != "bank" && type != "ewallet")
            return BadRequest("Type chỉ nhận cash, bank hoặc ewallet.");

        var initialBalance = request.InitialBalance ?? 0;
        if (initialBalance < 0)
            return BadRequest("Số dư ban đầu không được âm.");
        if (initialBalance > MaxMoneyValue)
            return BadRequest("Số dư ban đầu không được vượt quá 1.000.000.000.000 VND.");

        var wallet = new Wallet
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Type = type,
            InitialBalance = initialBalance,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Wallets.Add(wallet);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            wallet.Id,
            wallet.Name,
            wallet.Type,
            wallet.InitialBalance,
            CurrentBalance = wallet.InitialBalance,
            wallet.CreatedAtUtc
        });
    }

    // Sửa ví
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] WalletRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
            return NotFound("Không tìm thấy ví.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tên ví không được để trống.");

        var type = wallet.Type;
        if (request.Type != null)
        {
            if (string.IsNullOrWhiteSpace(request.Type))
                return BadRequest("Type không được để trống.");

            type = request.Type.Trim().ToLowerInvariant();
            if (type != "cash" && type != "bank" && type != "ewallet")
                return BadRequest("Type chỉ nhận cash, bank hoặc ewallet.");
        }

        var initialBalanceWasSupplied = request.InitialBalance.HasValue;
        var initialBalance = request.InitialBalance ?? wallet.InitialBalance;

        if (initialBalance < 0)
            return BadRequest("Số dư ban đầu không được âm.");
        if (initialBalance > MaxMoneyValue)
            return BadRequest("Số dư ban đầu không được vượt quá 1.000.000.000.000 VND.");

        var hasTransactions = await _db.FamilyTransactions.AnyAsync(t =>
            t.UserId == userId &&
            (t.WalletId == id || t.ToWalletId == id));

        if (hasTransactions &&
            initialBalanceWasSupplied &&
            wallet.InitialBalance != initialBalance)
        {
            return BadRequest(
                "Không thể thay đổi số dư ban đầu sau khi ví đã có giao dịch. Hãy tạo giao dịch điều chỉnh số dư.");
        }

        wallet.Name = request.Name.Trim();
        wallet.Type = type;
        if (initialBalanceWasSupplied)
            wallet.InitialBalance = initialBalance;

        await _db.SaveChangesAsync();

        var transactions = await _db.FamilyTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId &&
                        (t.WalletId == id || t.ToWalletId == id))
            .Select(t => new { t.Amount, t.Type, t.WalletId, t.ToWalletId })
            .ToListAsync();

        var currentBalance =
            wallet.InitialBalance +
            transactions
                .Where(t => t.Type == "income" && t.WalletId == id)
                .Sum(t => t.Amount) -
            transactions
                .Where(t => t.Type == "expense" && t.WalletId == id)
                .Sum(t => t.Amount) -
            transactions
                .Where(t => t.Type == "transfer" && t.WalletId == id)
                .Sum(t => t.Amount) +
            transactions
                .Where(t => t.Type == "transfer" && t.ToWalletId == id)
                .Sum(t => t.Amount);

        return Ok(new
        {
            wallet.Id,
            wallet.Name,
            wallet.Type,
            wallet.InitialBalance,
            CurrentBalance = currentBalance,
            wallet.CreatedAtUtc
        });
    }

    // Xóa ví; giao dịch cũ vẫn được giữ lại
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var wallet = await _db.Wallets
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

        if (wallet == null)
            return NotFound("Không tìm thấy ví.");

        _db.Wallets.Remove(wallet);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public class WalletRequest
{
    public string Name { get; set; } = "";
    public string? Type { get; set; }
    public long? InitialBalance { get; set; }
}