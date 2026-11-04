using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    // Ví dụ: GET /api/reports/monthly?year=2026&month=11
    // Báo cáo gia đình: thêm &familyGroupId=1
    [HttpGet("monthly")]
    public async Task<IActionResult> GetMonthly(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] int? familyGroupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var current = VietnamTime.GetCurrentYearMonth();
        var reportYear = year ?? current.Year;
        var reportMonth = month ?? current.Month;

        if (reportYear < 2000 || reportYear > 2100)
            return BadRequest("Năm phải nằm trong khoảng từ 2000 đến 2100.");

        if (reportMonth < 1 || reportMonth > 12)
            return BadRequest("Tháng phải nằm trong khoảng từ 1 đến 12.");

        var (start, end) = VietnamTime.GetMonthUtcRange(reportYear, reportMonth);

        var query = _db.FamilyTransactions
            .AsNoTracking()
            .Where(t =>
                t.OccurredAtUtc >= start &&
                t.OccurredAtUtc < end &&
                t.Type != "transfer" &&
                t.Type != "adjustment");

        if (familyGroupId.HasValue)
        {
            var isMember = await _db.FamilyMembers.AnyAsync(m =>
                m.UserId == userId &&
                m.FamilyGroupId == familyGroupId.Value);

            if (!isMember)
                return Forbid();

            query = query.Where(t =>
                t.FamilyGroupId == familyGroupId.Value);
        }
        else
        {
            query = query.Where(t => t.UserId == userId);
        }

        var transactions = await query
            .Select(t => new
            {
                t.Amount,
                t.Type,
                t.CategoryName,
                t.OccurredAtUtc
            })
            .ToListAsync();

        var totalIncome = transactions
            .Where(t => t.Type == "income")
            .Sum(t => t.Amount);

        var totalExpense = transactions
            .Where(t => t.Type == "expense")
            .Sum(t => t.Amount);

        var categories = transactions
            .GroupBy(t => new { t.CategoryName, t.Type })
            .Select(group => new
            {
                categoryName = group.Key.CategoryName,
                type = group.Key.Type,
                amount = group.Sum(t => t.Amount)
            })
            .OrderByDescending(item => item.amount)
            .ToList();

        var breakdown = new
        {
            income = categories
                .Where(item => item.type == "income")
                .Select(item => new
                {
                    categoryName = item.categoryName,
                    amount = item.amount
                })
                .ToList(),
            expense = categories
                .Where(item => item.type == "expense")
                .Select(item => new
                {
                    categoryName = item.categoryName,
                    amount = item.amount
                })
                .ToList()
        };

        var daily = transactions
            .GroupBy(t => VietnamTime.ToVietnamDateTimeUtc(t.OccurredAtUtc).Date)
            .Select(group => new
            {
                date = group.Key,
                income = group
                    .Where(t => t.Type == "income")
                    .Sum(t => t.Amount),
                expense = group
                    .Where(t => t.Type == "expense")
                    .Sum(t => t.Amount)
            })
            .OrderBy(item => item.date)
            .ToList();

        return Ok(new
        {
            year = reportYear,
            month = reportMonth,
            familyGroupId,
            totalIncome,
            totalExpense,
            netAmount = totalIncome - totalExpense,
            transactionCount = transactions.Count,
            categories,
            breakdown,
            daily
        });
    }
}