using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;

namespace SmartMoney.Api.Services;

public class BudgetAlertService
{
    private static readonly ConcurrentDictionary<(int FamilyGroupId, int Year, int Month), SemaphoreSlim> EvaluationLocks = new();
    private readonly AppDbContext _db;
    private readonly PushNotificationService _push;
    private readonly ILogger<BudgetAlertService> _logger;

    public BudgetAlertService(
        AppDbContext db,
        PushNotificationService push,
        ILogger<BudgetAlertService> logger)
    {
        _db = db;
        _push = push;
        _logger = logger;
    }

    public async Task EvaluateAsync(int familyGroupId, int year, int month)
    {
        var key = (familyGroupId, year, month);
        var gate = EvaluationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await EvaluateLockedAsync(familyGroupId, year, month);
        }
        catch (Exception exception)
        {
            // The budget/transaction write has already committed; an alert failure must not
            // turn a successful API call into a 500 response.
            _logger.LogWarning(
                exception,
                "Không thể đánh giá cảnh báo ngân sách cho nhóm {FamilyGroupId}, tháng {Month}/{Year}.",
                familyGroupId,
                month,
                year);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task EvaluateLockedAsync(int familyGroupId, int year, int month)
    {
        var budget = await _db.FamilyBudgets.FirstOrDefaultAsync(b =>
            b.FamilyGroupId == familyGroupId &&
            b.Year == year &&
            b.Month == month);

        if (budget == null || budget.MonthlyLimit <= 0)
            return;

        var (start, end) = VietnamTime.GetMonthUtcRange(year, month);
        var spent = await _db.FamilyTransactions
            .Where(t =>
                t.FamilyGroupId == familyGroupId &&
                t.Type == "expense" &&
                t.OccurredAtUtc >= start &&
                t.OccurredAtUtc < end)
            .Select(t => (long?)t.Amount)
            .SumAsync() ?? 0;

        var warningThreshold = budget.MonthlyLimit * 0.8m;
        var needsSave = false;

        if (spent < warningThreshold)
        {
            if (budget.Warning80Sent || budget.Exceeded100Sent)
            {
                budget.Warning80Sent = false;
                budget.Exceeded100Sent = false;
                needsSave = true;
            }
        }
        else if (spent < budget.MonthlyLimit && budget.Exceeded100Sent)
        {
            budget.Exceeded100Sent = false;
            needsSave = true;
        }

        if (needsSave)
            await _db.SaveChangesAsync();

        if (!_push.IsConfigured)
            return;

        var memberUserIds = await _db.FamilyMembers
            .Where(m => m.FamilyGroupId == familyGroupId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();

        if (memberUserIds.Count == 0)
            return;

        var tokens = await _db.DeviceTokens
            .Where(d => memberUserIds.Contains(d.UserId))
            .Select(d => d.Token)
            .Distinct()
            .ToListAsync();

        if (tokens.Count == 0)
            return;

        if (spent >= budget.MonthlyLimit && !budget.Exceeded100Sent)
        {
            var sent = await SendAsync(
                tokens,
                "Đã vượt hạn mức",
                $"Nhóm đã chi {spent:N0} / {budget.MonthlyLimit:N0} cho tháng {month}/{year}.");

            if (sent > 0)
            {
                budget.Exceeded100Sent = true;
                budget.Warning80Sent = true;
                await _db.SaveChangesAsync();
            }

            return;
        }

        if (spent >= budget.MonthlyLimit * 0.8m &&
            !budget.Warning80Sent &&
            !budget.Exceeded100Sent)
        {
            var sent = await SendAsync(
                tokens,
                "Hạn mức gia đình sắp hết",
                $"Nhóm đã sử dụng ít nhất 80% hạn mức tháng {month}/{year}.");

            if (sent > 0)
            {
                budget.Warning80Sent = true;
                await _db.SaveChangesAsync();
            }
        }
    }

    private async Task<int> SendAsync(
        IReadOnlyCollection<string> tokens,
        string title,
        string body)
    {
        var sent = 0;

        foreach (var token in tokens)
        {
            try
            {
                await _push.SendAsync(token, title, body);
                sent++;
            }
            catch (Exception exception)
            {
                if (PushNotificationService.IsUnregistered(exception))
                {
                    var expired = await _db.DeviceTokens.Where(d => d.Token == token).ToListAsync();
                    _db.DeviceTokens.RemoveRange(expired);
                    await _db.SaveChangesAsync();
                }
                // Không đánh dấu đã gửi nếu Firebase trả lỗi.
            }
        }

        return sent;
    }
}
