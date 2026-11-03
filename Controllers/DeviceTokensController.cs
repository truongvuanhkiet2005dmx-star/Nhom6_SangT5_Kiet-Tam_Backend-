using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public class DeviceTokensController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PushNotificationService _push;

    public DeviceTokensController(
        AppDbContext db,
        PushNotificationService push)
    {
        _db = db;
        _push = push;
    }

    // Lưu hoặc cập nhật token điện thoại
    [HttpPost("token")]
    public async Task<IActionResult> SaveToken(
        [FromBody] DeviceTokenRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest("Token không được để trống.");

        var token = request.Token.Trim();

        var deviceToken = await _db.DeviceTokens.FirstOrDefaultAsync(d =>
            d.UserId == userId && d.Token == token);

        if (deviceToken == null)
        {
            deviceToken = new DeviceToken
            {
                UserId = userId,
                Token = token,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _db.DeviceTokens.Add(deviceToken);
        }
        else
        {
            deviceToken.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Đã lưu token thiết bị." });
    }

    // Xóa token khi người dùng đăng xuất hoặc không muốn nhận thông báo
    [HttpDelete("token")]
    public async Task<IActionResult> DeleteToken(
        [FromBody] DeviceTokenRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var deviceToken = await _db.DeviceTokens.FirstOrDefaultAsync(d =>
            d.UserId == userId && d.Token == request.Token);

        if (deviceToken == null)
            return NotFound("Không tìm thấy token thiết bị.");

        _db.DeviceTokens.Remove(deviceToken);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // Gửi thông báo thử đến các thiết bị đã lưu token của tài khoản
    [HttpPost("test-notification")]
    public async Task<IActionResult> SendTestNotification()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (!_push.IsConfigured)
            return BadRequest(
                "Backend chưa cấu hình Firebase Admin service account.");

        var tokens = await _db.DeviceTokens
            .Where(d => d.UserId == userId)
            .Select(d => d.Token)
            .ToListAsync();

        if (tokens.Count == 0)
            return BadRequest("Tài khoản chưa đăng ký token điện thoại.");

        var sentCount = 0;

        foreach (var token in tokens)
        {
            try
            {
                await _push.SendAsync(
                    token,
                    "SmartMoney",
                    "Thông báo thử từ backend.");

                sentCount++;
            }
            catch
            {
                // Token lỗi sẽ không được tính vào số gửi thành công.
            }
        }

        return Ok(new
        {
            message = "Đã gửi yêu cầu thông báo đến Firebase.",
            sentCount,
            totalDevices = tokens.Count
        });
    }
}

public class DeviceTokenRequest
{
    public string Token { get; set; } = "";
}