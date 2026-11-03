using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BudgetAlertService _alerts;

    public TransactionsController(
        AppDbContext db,
        BudgetAlertService alerts)
    {
        _db = db;
        _alerts = alerts;
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

        if (request.Type != "income" &&
            request.Type != "expense" &&
            request.Type != "transfer")
            return BadRequest("Type chỉ nhận income, expense hoặc transfer.");

        if (string.IsNullOrWhiteSpace(request.EventId))
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "EventId bắt buộc",
                Detail = "EventId là khóa đồng bộ ổn định; vui lòng gửi cùng EventId khi retry."
            });

        if (request.WalletId.HasValue)
        {
            var ownsWallet = await _db.Wallets.AnyAsync(w =>
                w.Id == request.WalletId.Value && w.UserId == userId);

            if (!ownsWallet)
                return BadRequest("Ví không tồn tại hoặc không thuộc tài khoản của bạn.");
        }

        if (request.Type == "transfer")
        {
            if (!request.WalletId.HasValue || !request.ToWalletId.HasValue)
                return BadRequest("Chuyển khoản cần có WalletId nguồn và ToWalletId đích.");

            if (request.WalletId.Value == request.ToWalletId.Value)
                return BadRequest("Ví nguồn và ví đích phải khác nhau.");

            var ownedWalletCount = await _db.Wallets.CountAsync(w =>
                w.UserId == userId &&
                (w.Id == request.WalletId.Value || w.Id == request.ToWalletId.Value));

            if (ownedWalletCount != 2)
                return BadRequest("Ví nguồn hoặc ví đích không thuộc tài khoản của bạn.");
        }
        else if (request.ToWalletId.HasValue)
        {
            return BadRequest("ToWalletId chỉ được dùng khi Type là transfer.");
        }

        if (request.FamilyGroupId.HasValue)
        {
            var isMember = await _db.FamilyMembers.AnyAsync(m =>
                m.UserId == userId &&
                m.FamilyGroupId == request.FamilyGroupId.Value);

            if (!isMember)
                return Forbid();
        }

        var eventId = request.EventId.Trim();

        var existing = await _db.FamilyTransactions.FirstOrDefaultAsync(t =>
            t.UserId == userId && t.EventId == eventId);

        if (existing != null)
        {
            if (TransactionsMatch(existing, request))
                return Ok(existing);

            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "EventId trùng nhưng dữ liệu khác",
                Detail = "EventId đã tồn tại nhưng dữ liệu giao dịch không khớp với bản ghi trước đó."
            });
        }

        var transaction = new FamilyTransaction
        {
            EventId = eventId,
            UserId = userId,
            FamilyGroupId = request.FamilyGroupId,
            WalletId = request.WalletId,
            ToWalletId = request.Type == "transfer" ? request.ToWalletId : null,
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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.Entry(transaction).State = EntityState.Detached;

            var concurrentExisting = await _db.FamilyTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.UserId == userId && t.EventId == eventId);

            if (concurrentExisting != null)
            {
                if (TransactionsMatch(concurrentExisting, request))
                    return Ok(concurrentExisting);

                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "EventId trùng nhưng dữ liệu khác",
                    Detail = "EventId đã tồn tại nhưng dữ liệu giao dịch không khớp với bản ghi trước đó."
                });
            }

            throw;
        }

        if (transaction.FamilyGroupId.HasValue)
        {
            var occurred = transaction.OccurredAtUtc;
            await _alerts.EvaluateAsync(
                transaction.FamilyGroupId.Value,
                occurred.Year,
                occurred.Month);
        }

        return Ok(transaction);
    }

    private static bool TransactionsMatch(
        FamilyTransaction transaction,
        TransactionRequest request)
    {
        if (transaction.FamilyGroupId != request.FamilyGroupId ||
            transaction.WalletId != request.WalletId ||
            transaction.ToWalletId != request.ToWalletId ||
            transaction.Amount != request.Amount ||
            !string.Equals(transaction.Type, request.Type, StringComparison.Ordinal) ||
            !string.Equals(
                transaction.CategoryName,
                request.CategoryName ?? "Khác",
                StringComparison.Ordinal) ||
            !string.Equals(transaction.Source, request.Source ?? "", StringComparison.Ordinal) ||
            !string.Equals(transaction.BankName, request.BankName ?? "", StringComparison.Ordinal) ||
            !string.Equals(transaction.PackageName, request.PackageName ?? "", StringComparison.Ordinal) ||
            !string.Equals(transaction.Note, request.Note ?? "", StringComparison.Ordinal))
        {
            return false;
        }

        return !request.OccurredAtUtc.HasValue ||
               transaction.OccurredAtUtc == request.OccurredAtUtc.Value.ToUniversalTime();
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

        if (request.Type != "income" &&
            request.Type != "expense" &&
            request.Type != "transfer")
            return BadRequest("Type chỉ nhận income, expense hoặc transfer.");

        if (!string.IsNullOrWhiteSpace(request.EventId) &&
            !string.Equals(transaction.EventId, request.EventId.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Không thể đổi EventId",
                Detail = "EventId là khóa đồng bộ; API bỏ qua hoặc thay đổi EventId sẽ không được xử lý."
            });
        }

        if (request.WalletId.HasValue)
        {
            var ownsWallet = await _db.Wallets.AnyAsync(w =>
                w.Id == request.WalletId.Value && w.UserId == userId);

            if (!ownsWallet)
                return BadRequest("Ví không tồn tại hoặc không thuộc tài khoản của bạn.");
        }

        if (request.Type == "transfer")
        {
            if (!request.WalletId.HasValue || !request.ToWalletId.HasValue)
                return BadRequest("Chuyển khoản cần có WalletId nguồn và ToWalletId đích.");

            if (request.WalletId.Value == request.ToWalletId.Value)
                return BadRequest("Ví nguồn và ví đích phải khác nhau.");

            var ownedWalletCount = await _db.Wallets.CountAsync(w =>
                w.UserId == userId &&
                (w.Id == request.WalletId.Value || w.Id == request.ToWalletId.Value));

            if (ownedWalletCount != 2)
                return BadRequest("Ví nguồn hoặc ví đích không thuộc tài khoản của bạn.");
        }
        else if (request.ToWalletId.HasValue)
        {
            return BadRequest("ToWalletId chỉ được dùng khi Type là transfer.");
        }

        if (request.FamilyGroupId.HasValue)
        {
            var isMember = await _db.FamilyMembers.AnyAsync(m =>
                m.UserId == userId &&
                m.FamilyGroupId == request.FamilyGroupId.Value);

            if (!isMember)
                return Forbid();
        }

        var oldGroupId = transaction.FamilyGroupId;
        var oldOccurredAtUtc = transaction.OccurredAtUtc;

        transaction.FamilyGroupId = request.FamilyGroupId;
        transaction.WalletId = request.WalletId;
        transaction.ToWalletId =
            request.Type == "transfer" ? request.ToWalletId : null;
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

        var affectedMonths = new HashSet<(int GroupId, int Year, int Month)>();

        if (oldGroupId.HasValue)
        {
            affectedMonths.Add((
                oldGroupId.Value,
                oldOccurredAtUtc.Year,
                oldOccurredAtUtc.Month));
        }

        if (transaction.FamilyGroupId.HasValue)
        {
            affectedMonths.Add((
                transaction.FamilyGroupId.Value,
                transaction.OccurredAtUtc.Year,
                transaction.OccurredAtUtc.Month));
        }

        foreach (var affected in affectedMonths)
        {
            await _alerts.EvaluateAsync(
                affected.GroupId,
                affected.Year,
                affected.Month);
        }

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

        if (transaction.FamilyGroupId.HasValue)
        {
            await _alerts.EvaluateAsync(
                transaction.FamilyGroupId.Value,
                transaction.OccurredAtUtc.Year,
                transaction.OccurredAtUtc.Month);
        }

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
    public int? ToWalletId { get; set; }
}