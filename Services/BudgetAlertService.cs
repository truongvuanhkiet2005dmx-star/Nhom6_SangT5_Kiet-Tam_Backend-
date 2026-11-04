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
        NotificationPlan? notification = null;
        try
        {
            notification = await EvaluateLockedAsync(familyGroupId, year, month);
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

        if (notification != null)
        {
            try
            {
                await SendNotificationAsync(notification);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Không thể gửi cảnh báo ngân sách cho nhóm {FamilyGroupId}, tháng {Month}/{Year}.",
                    familyGroupId,
                    month,
                    year);
            }
        }
    }

    private async Task<NotificationPlan?> EvaluateLockedAsync(int familyGroupId, int year, int month)
    {
        var budget = await _db.FamilyBudgets.FirstOrDefaultAsync(b =>
            b.FamilyGroupId == familyGroupId &&
            b.Year == year &&
            b.Month == month);

        if (budget == null || budget.MonthlyLimit <= 0)
            return null;

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
            return null;

        var memberUserIds = await _db.FamilyMembers
            .Where(m => m.FamilyGroupId == familyGroupId)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();

        if (memberUserIds.Count == 0)
            return null;

        var tokens = await _db.DeviceTokens
            .Where(d => memberUserIds.Contains(d.UserId))
            .Select(d => d.Token)
            .Distinct()
            .ToListAsync();

        if (tokens.Count == 0)
            return null;

        if (spent >= budget.MonthlyLimit && !budget.Exceeded100Sent)
        {
            var claimed = await _db.FamilyBudgets
                .Where(b => b.Id == budget.Id && !b.Exceeded100Sent)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Exceeded100Sent, true)
                    .SetProperty(b => b.Warning80Sent, true)) > 0;

            if (!claimed)
                return null;

            return new NotificationPlan(
                "Đã vượt hạn mức",
                $"Nhóm đã chi {spent:N0} / {budget.MonthlyLimit:N0} cho tháng {month}/{year}.",
                tokens,
                budget.Id,
                Exceeded: true,
                Warning: true);
        }

        if (spent >= budget.MonthlyLimit * 0.8m &&
            !budget.Warning80Sent &&
            !budget.Exceeded100Sent)
        {
            var claimed = await _db.FamilyBudgets
                .Where(b => b.Id == budget.Id &&
                            !b.Warning80Sent &&
                            !b.Exceeded100Sent)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Warning80Sent, true)) > 0;

            if (!claimed)
                return null;

            return new NotificationPlan(
                "Hạn mức gia đình sắp hết",
                $"Nhóm đã sử dụng ít nhất 80% hạn mức tháng {month}/{year}.",
                tokens,
                budget.Id,
                Exceeded: false,
                Warning: true);
        }

        return null;
    }

    private async Task SendNotificationAsync(NotificationPlan notification)
    {
        var sent = await SendAsync(
            notification.Tokens,
            notification.Title,
            notification.Body);

        if (sent > 0)
            return;

        if (notification.Exceeded)
        {
            await _db.FamilyBudgets
                .Where(b => b.Id == notification.BudgetId &&
                            b.Exceeded100Sent &&
                            b.Warning80Sent)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Exceeded100Sent, false)
                    .SetProperty(b => b.Warning80Sent, false));
        }
        else if (notification.Warning)
        {
            await _db.FamilyBudgets
                .Where(b => b.Id == notification.BudgetId &&
                            b.Warning80Sent &&
                            !b.Exceeded100Sent)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Warning80Sent, false));
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

    private sealed record NotificationPlan(
        string Title,
        string Body,
        IReadOnlyCollection<string> Tokens,
        int BudgetId,
        bool Exceeded,
        bool Warning);
}
