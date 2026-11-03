using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;

namespace SmartMoney.Api.Services;

public class BudgetAlertService
{
    private readonly AppDbContext _db;
    private readonly PushNotificationService _push;

    public BudgetAlertService(
        AppDbContext db,
        PushNotificationService push)
    {
        _db = db;
        _push = push;
    }

    public async Task EvaluateAsync(int familyGroupId, int year, int month)
    {
        var budget = await _db.FamilyBudgets.FirstOrDefaultAsync(b =>
            b.FamilyGroupId == familyGroupId &&
            b.Year == year &&
            b.Month == month);

        if (budget == null || !_push.IsConfigured || budget.MonthlyLimit <= 0)
            return;

        if (budget.Exceeded100Sent)
            return;

        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var spent = await _db.FamilyTransactions
            .Where(t =>
                t.FamilyGroupId == familyGroupId &&
                t.Type == "expense" &&
                t.OccurredAtUtc >= start &&
                t.OccurredAtUtc < end)
            .Select(t => (long?)t.Amount)
            .SumAsync() ?? 0;

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
            catch
            {
                // Không đánh dấu đã gửi nếu Firebase trả lỗi.
            }
        }

        return sent;
    }
}
