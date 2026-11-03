using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class GoogleAuthController : ControllerBase
{
    private readonly IServiceProvider _services;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenService _jwtTokens;

    public GoogleAuthController(
        IServiceProvider services,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwtTokens)
    {
        _services = services;
        _userManager = userManager;
        _jwtTokens = jwtTokens;
    }

    [HttpPost("google")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GoogleLogin(
        [FromBody] GoogleLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return BadRequest("Firebase ID token không được để trống.");

        var firebaseAuth = _services.GetService<FirebaseAuth>();
        if (firebaseAuth == null)
            return StatusCode(
                503,
                "Backend chưa cấu hình Firebase service account.");

        FirebaseToken decodedToken;

        try
        {
            decodedToken = await firebaseAuth.VerifyIdTokenAsync(request.IdToken);
        }
        catch
        {
            return Unauthorized("Firebase ID token không hợp lệ hoặc đã hết hạn.");
        }

        var email = decodedToken.Claims.TryGetValue("email", out var emailValue)
            ? emailValue?.ToString()
            : null;

        var emailVerified = decodedToken.Claims.TryGetValue("email_verified", out var verifiedValue)
            && (verifiedValue is true ||
                string.Equals(verifiedValue?.ToString(), "true", StringComparison.OrdinalIgnoreCase));

        var displayName =
            decodedToken.Claims.TryGetValue("name", out var nameValue)
                ? nameValue?.ToString()
                : null;

        if (string.IsNullOrWhiteSpace(email))
            return Unauthorized("Tài khoản Firebase không có email.");

        if (!emailVerified)
            return Unauthorized("Email của tài khoản Google chưa được xác minh.");

        var user = await _userManager.FindByEmailAsync(email);
        var normalizedDisplayName = displayName?.Trim() ?? "";

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(normalizedDisplayName)
                    ? email
                    : normalizedDisplayName
            };

            var createResult = await _userManager.CreateAsync(user);

            if (!createResult.Succeeded)
            {
                return Conflict(new
                {
                    message = "Không thể tạo tài khoản Google hoặc email đã được đăng ký đồng thời. Không tự động liên kết tài khoản.",
                    errors = createResult.Errors.Select(e => e.Description)
                });
            }
        else
        {
            return Conflict(new
            {
                message = "Email này đã tồn tại. Liên kết tài khoản Google bằng mật khẩu chưa được hỗ trợ trong phiên bản này."
            });
        }
        }

        return Ok(await _jwtTokens.CreateResponseAsync(user));
    }
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = "";
}