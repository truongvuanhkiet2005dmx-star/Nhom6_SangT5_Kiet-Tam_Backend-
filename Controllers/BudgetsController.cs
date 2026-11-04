using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/budgets")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private const long MaxMoneyValue = 1_000_000_000_000L;
    private const int MinYear = 2000;
    private const int MaxYear = 2100;
    private readonly AppDbContext _db;
    private readonly BudgetAlertService _alerts;
    private readonly ILogger<BudgetsController> _logger;

    public BudgetsController(
        AppDbContext db,
        BudgetAlertService alerts,
        ILogger<BudgetsController> logger)
    {
        _db = db;
        _alerts = alerts;
        _logger = logger;
    }

    // Xem hạn mức và tình trạng chi tiêu
    // GET /api/budgets/1?year=2026&month=11
    [HttpGet("{familyGroupId:int}")]
    public async Task<IActionResult> Get(
        int familyGroupId,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var isMember = await _db.FamilyMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.FamilyGroupId == familyGroupId);

        if (!isMember)
            return Forbid();

        var current = VietnamTime.GetCurrentYearMonth();
        var budgetYear = year ?? current.Year;
        var budgetMonth = month ?? current.Month;

        if (budgetYear < MinYear || budgetYear > MaxYear)
            return BadRequest("Năm phải nằm trong khoảng từ 2000 đến 2100.");

        if (budgetMonth < 1 || budgetMonth > 12)
            return BadRequest("Tháng phải nằm trong khoảng từ 1 đến 12.");

        var budget = await _db.FamilyBudgets.FirstOrDefaultAsync(b =>
            b.FamilyGroupId == familyGroupId &&
            b.Year == budgetYear &&
            b.Month == budgetMonth);

        if (budget == null)
            return NotFound("Tháng này chưa được đặt hạn mức.");

        var (start, end) = VietnamTime.GetMonthUtcRange(budgetYear, budgetMonth);

        var spent = await _db.FamilyTransactions
            .Where(t =>
                t.FamilyGroupId == familyGroupId &&
                t.Type == "expense" &&
                t.OccurredAtUtc >= start &&
                t.OccurredAtUtc < end)
            .Select(t => (long?)t.Amount)
            .SumAsync() ?? 0;

        var percent = budget.MonthlyLimit > 0
            ? Math.Round(spent * 100m / budget.MonthlyLimit, 2)
            : 0;

        var alertLevel = spent >= budget.MonthlyLimit
            ? "exceeded100"
            : spent >= budget.MonthlyLimit * 0.8m
                ? "warning80"
                : "normal";

        return Ok(new
        {
            familyGroupId,
            year = budgetYear,
            month = budgetMonth,
            budget.MonthlyLimit,
            spent,
            remaining = budget.MonthlyLimit - spent,
            percent,
            alertLevel
        });
    }

    // Chủ nhóm đặt hoặc cập nhật hạn mức
    // PUT /api/budgets/1
    [HttpPut("{familyGroupId:int}")]
    public async Task<IActionResult> Set(
        int familyGroupId,
        [FromBody] BudgetRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (request.Month < 1 || request.Month > 12)
            return BadRequest("Tháng phải nằm trong khoảng từ 1 đến 12.");
        if (request.Year < MinYear || request.Year > MaxYear)
            return BadRequest("Năm phải nằm trong khoảng từ 2000 đến 2100.");

        if (request.MonthlyLimit <= 0)
            return BadRequest("Hạn mức phải lớn hơn 0.");
        if (request.MonthlyLimit > MaxMoneyValue)
            return BadRequest("Hạn mức không được vượt quá 1.000.000.000.000 VND.");

        var member = await _db.FamilyMembers.FirstOrDefaultAsync(m =>
            m.UserId == userId &&
            m.FamilyGroupId == familyGroupId);

        if (member == null)
            return Forbid();

        if (!string.Equals(member.AccessRole, "Owner", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var budget = await _db.FamilyBudgets.FirstOrDefaultAsync(b =>
            b.FamilyGroupId == familyGroupId &&
            b.Year == request.Year &&
            b.Month == request.Month);

        if (budget == null)
        {
            budget = new FamilyBudget
            {
                FamilyGroupId = familyGroupId,
                Year = request.Year,
                Month = request.Month,
                MonthlyLimit = request.MonthlyLimit,
                Warning80Sent = false,
                Exceeded100Sent = false
            };

            _db.FamilyBudgets.Add(budget);
        }
        else
        {
            budget.MonthlyLimit = request.MonthlyLimit;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (_db.Entry(budget).State != EntityState.Detached)
            {
                _db.Entry(budget).State = EntityState.Detached;
            }

            var concurrentBudget = await _db.FamilyBudgets
                .FirstOrDefaultAsync(b =>
                    b.FamilyGroupId == familyGroupId &&
                    b.Year == request.Year &&
                    b.Month == request.Month);

            if (concurrentBudget == null)
                throw;

            concurrentBudget.MonthlyLimit = request.MonthlyLimit;
            await _db.SaveChangesAsync();
            budget = concurrentBudget;
        }

        try
        {
            await _alerts.EvaluateAsync(
                familyGroupId,
                budget.Year,
                budget.Month);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Hạn mức đã được lưu nhưng cảnh báo ngân sách không thể đánh giá.");
        }

        return Ok(new
        {
            message = "Đã lưu hạn mức tháng.",
            budget.FamilyGroupId,
            budget.Year,
            budget.Month,
            budget.MonthlyLimit
        });
    }
}

public class BudgetRequest
{
    public int Year { get; set; }
    public int Month { get; set; }
    public long MonthlyLimit { get; set; }
}