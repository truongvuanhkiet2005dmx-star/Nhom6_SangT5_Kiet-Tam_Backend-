using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TransactionsController(AppDbContext db)
    {
        _db = db;
    }

    // Lấy giao dịch cá nhân và giao dịch được chia sẻ trong gia đình
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var familyIds = await _db.FamilyMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.FamilyGroupId)
            .ToListAsync();

        var query = _db.FamilyTransactions
            .AsNoTracking()
            .Where(t =>
                t.UserId == userId ||
                (t.FamilyGroupId.HasValue &&
                 familyIds.Contains(t.FamilyGroupId.Value)));

        if (year.HasValue)
            query = query.Where(t => t.OccurredAtUtc.Year == year.Value);

        if (month.HasValue)
            query = query.Where(t => t.OccurredAtUtc.Month == month.Value);

        var transactions = await query
            .OrderByDescending(t => t.OccurredAtUtc)
            .ToListAsync();

        return Ok(transactions);
    }

    // Tạo giao dịch
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] TransactionRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (request.Amount <= 0)
            return BadRequest("Số tiền phải lớn hơn 0.");

        if (request.Type != "income" && request.Type != "expense")
            return BadRequest("Type chỉ nhận income hoặc expense.");

        if (request.WalletId.HasValue)
        {
            var ownsWallet = await _db.Wallets.AnyAsync(w =>
                w.Id == request.WalletId.Value && w.UserId == userId);

            if (!ownsWallet)
                return BadRequest("Ví không tồn tại hoặc không thuộc tài khoản của bạn.");
        }

        if (request.FamilyGroupId.HasValue)
        {
            var isMember = await _db.FamilyMembers.AnyAsync(m =>
                m.UserId == userId &&
                m.FamilyGroupId == request.FamilyGroupId.Value);

            if (!isMember)
                return Forbid();
        }

        var transaction = new FamilyTransaction
        {
            EventId = string.IsNullOrWhiteSpace(request.EventId)
                ? Guid.NewGuid().ToString()
                : request.EventId.Trim(),
            UserId = userId,
            FamilyGroupId = request.FamilyGroupId,
            WalletId = request.WalletId,
            Amount = request.Amount,
            Type = request.Type,
            CategoryName = request.CategoryName ?? "Khác",
            Source = request.Source ?? "",
            BankName = request.BankName ?? "",
            PackageName = request.PackageName ?? "",
            OccurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow,
            Note = request.Note ?? ""
        };

        _db.FamilyTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        return Ok(transaction);
    }

    // Sửa giao dịch của chính mình
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] TransactionRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var transaction = await _db.FamilyTransactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (transaction == null)
            return NotFound("Không tìm thấy giao dịch của bạn.");

        if (request.Amount <= 0)
            return BadRequest("Số tiền phải lớn hơn 0.");

        if (request.Type != "income" && request.Type != "expense")
            return BadRequest("Type chỉ nhận income hoặc expense.");

        if (request.WalletId.HasValue)
        {
            var ownsWallet = await _db.Wallets.AnyAsync(w =>
                w.Id == request.WalletId.Value && w.UserId == userId);

            if (!ownsWallet)
                return BadRequest("Ví không tồn tại hoặc không thuộc tài khoản của bạn.");
        }

        if (request.FamilyGroupId.HasValue)
        {
            var isMember = await _db.FamilyMembers.AnyAsync(m =>
                m.UserId == userId &&
                m.FamilyGroupId == request.FamilyGroupId.Value);

            if (!isMember)
                return Forbid();
        }

        transaction.FamilyGroupId = request.FamilyGroupId;
        transaction.WalletId = request.WalletId;
        transaction.Amount = request.Amount;
        transaction.Type = request.Type;
        transaction.CategoryName = request.CategoryName ?? "Khác";
        transaction.Source = request.Source ?? "";
        transaction.BankName = request.BankName ?? "";
        transaction.PackageName = request.PackageName ?? "";
        transaction.OccurredAtUtc =
            request.OccurredAtUtc ?? transaction.OccurredAtUtc;
        transaction.Note = request.Note ?? "";

        await _db.SaveChangesAsync();
        return Ok(transaction);
    }

    // Xóa giao dịch của chính mình
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var transaction = await _db.FamilyTransactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (transaction == null)
            return NotFound("Không tìm thấy giao dịch của bạn.");

        _db.FamilyTransactions.Remove(transaction);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public class TransactionRequest
{
    public string? EventId { get; set; }
    public long Amount { get; set; }
    public string Type { get; set; } = "expense";
    public string? CategoryName { get; set; }
    public string? Source { get; set; }
    public string? BankName { get; set; }
    public string? PackageName { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public string? Note { get; set; }
    public int? FamilyGroupId { get; set; }
    public int? WalletId { get; set; }
}