using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
public class WalletsController : ControllerBase
{
    private const long MaxMoneyValue = 1_000_000_000_000L;
    private readonly AppDbContext _db;
    private readonly WalletBalanceService _balances;

    public WalletsController(AppDbContext db, WalletBalanceService balances)
    {
        _db = db;
        _balances = balances;
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

        var balances = await _balances.CalculateCurrentBalancesAsync(wallets, userId);

        var result = new List<WalletResponse>();

        foreach (var wallet in wallets)
        {
            result.Add(new WalletResponse
            {
                Id = wallet.Id,
                Name = wallet.Name,
                Type = wallet.Type,
                InitialBalance = wallet.InitialBalance,
                CurrentBalance = balances[wallet.Id],
                ClientId = wallet.ClientId,
                CreatedAtUtc = wallet.CreatedAtUtc
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

        var currentBalance = await _balances.CalculateCurrentBalanceAsync(wallet, userId);
        return Ok(new WalletResponse
        {
            Id = wallet.Id,
            Name = wallet.Name,
            Type = wallet.Type,
            InitialBalance = wallet.InitialBalance,
            CurrentBalance = currentBalance,
            ClientId = wallet.ClientId,
            CreatedAtUtc = wallet.CreatedAtUtc
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
        if (request.Name.Trim().Length > 100)
            return BadRequest("Tên ví không được vượt quá 100 ký tự.");

        var type = request.Type.Trim().ToLowerInvariant();
        if (type != "cash" && type != "bank" && type != "ewallet")
            return BadRequest("Type chỉ nhận cash, bank hoặc ewallet.");

        var initialBalance = request.InitialBalance ?? 0;
        if (initialBalance < 0)
            return BadRequest("Số dư ban đầu không được âm.");
        if (initialBalance > MaxMoneyValue)
            return BadRequest("Số dư ban đầu không được vượt quá 1.000.000.000.000 VND.");

        var clientId = request.ClientId?.Trim();
        if (clientId is { Length: > 128 })
            return BadRequest("ClientId không được vượt quá 128 ký tự.");

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var existing = await _db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId && w.ClientId == clientId);
            if (existing != null)
            {
                if (existing.Name != request.Name.Trim() ||
                    existing.Type != type ||
                    existing.InitialBalance != initialBalance)
                    return Conflict(new ProblemDetails
                    {
                        Status = StatusCodes.Status409Conflict,
                        Title = "ClientId trùng nhưng dữ liệu ví khác",
                        Detail = "ClientId đã được đồng bộ với một ví có dữ liệu khác."
                    });
                return Ok(new WalletResponse
                {
                    Id = existing.Id,
                    Name = existing.Name,
                    Type = existing.Type,
                    InitialBalance = existing.InitialBalance,
                    CurrentBalance = await _balances.CalculateCurrentBalanceAsync(existing, userId),
                    ClientId = existing.ClientId,
                    CreatedAtUtc = existing.CreatedAtUtc
                });
            }
        }

        var wallet = new Wallet
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Type = type,
            InitialBalance = initialBalance,
            ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Wallets.Add(wallet);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.Entry(wallet).State = EntityState.Detached;
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var concurrent = await _db.Wallets.AsNoTracking()
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.ClientId == clientId);
                if (concurrent != null)
                    return Ok(new WalletResponse
                    {
                        Id = concurrent.Id,
                        Name = concurrent.Name,
                        Type = concurrent.Type,
                        InitialBalance = concurrent.InitialBalance,
                        CurrentBalance = await _balances.CalculateCurrentBalanceAsync(concurrent, userId),
                        ClientId = concurrent.ClientId,
                        CreatedAtUtc = concurrent.CreatedAtUtc
                    });
            }
            throw;
        }

        return Ok(new WalletResponse
        {
            Id = wallet.Id,
            Name = wallet.Name,
            Type = wallet.Type,
            InitialBalance = wallet.InitialBalance,
            CurrentBalance = wallet.InitialBalance,
            ClientId = wallet.ClientId,
            CreatedAtUtc = wallet.CreatedAtUtc
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

        var currentBalance = await _balances.CalculateCurrentBalanceAsync(wallet, userId);

        return Ok(new WalletResponse
        {
            Id = wallet.Id,
            Name = wallet.Name,
            Type = wallet.Type,
            InitialBalance = wallet.InitialBalance,
            CurrentBalance = currentBalance,
            ClientId = wallet.ClientId,
            CreatedAtUtc = wallet.CreatedAtUtc
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

        var hasTransactions = await _db.FamilyTransactions.AnyAsync(t =>
            t.WalletId == id || t.ToWalletId == id);
        if (hasTransactions)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Không thể xóa ví",
                Detail = "Ví đang được tham chiếu bởi giao dịch. Hãy giữ ví để bảo toàn lịch sử giao dịch."
            });
        }

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
    public string? ClientId { get; set; }
}

public class WalletResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public long InitialBalance { get; set; }
    public long CurrentBalance { get; set; }
    public string? ClientId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}