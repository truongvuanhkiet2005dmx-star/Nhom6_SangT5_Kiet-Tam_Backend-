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

        var firebaseUid = decodedToken.Uid;
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return Unauthorized("Firebase ID token không chứa UID hợp lệ.");

        const string provider = "Firebase";
        var linkedUser = await _userManager.FindByLoginAsync(provider, firebaseUid);
        if (linkedUser != null)
        {
            if (!string.Equals(linkedUser.Email?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
                return Conflict("Liên kết Google không khớp email của tài khoản.");

            return Ok(await _jwtTokens.CreateResponseAsync(linkedUser));
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser != null)
        {
            return Conflict(new
            {
                message = "Email đã tồn tại nhưng chưa được liên kết an toàn với tài khoản Google. Không cấp JWT chỉ dựa trên email."
            });
        }

        var normalizedDisplayName = displayName?.Trim() ?? "";
        var newUser = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(normalizedDisplayName)
                ? normalizedEmail
                : normalizedDisplayName,
            LockoutEnabled = true
        };

        var createResult = await _userManager.CreateAsync(newUser);
        if (!createResult.Succeeded)
        {
            var racedUser = await _userManager.FindByEmailAsync(normalizedEmail);
            var racedLogin = await _userManager.FindByLoginAsync(provider, firebaseUid);
            if (racedUser != null && racedLogin?.Id == racedUser.Id)
                return Ok(await _jwtTokens.CreateResponseAsync(racedUser));

            return Conflict(new
            {
                message = "Không thể xác minh liên kết Google tài khoản. Không cấp JWT chỉ dựa trên email.",
                errors = createResult.Errors.Select(error => error.Description)
            });
        }

        var loginResult = await _userManager.AddLoginAsync(
            newUser,
            new UserLoginInfo(provider, firebaseUid, "Firebase"));

        if (!loginResult.Succeeded)
        {
            var verifiedLogin = await _userManager.FindByLoginAsync(provider, firebaseUid);
            if (verifiedLogin?.Id == newUser.Id)
                return Ok(await _jwtTokens.CreateResponseAsync(verifiedLogin));

            return Conflict(new
            {
                message = "Tài khoản đã được tạo nhưng liên kết Firebase UID thất bại. Không cấp JWT.",
                errors = loginResult.Errors.Select(error => error.Description)
            });
        }

        return Ok(await _jwtTokens.CreateResponseAsync(newUser));
    }
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = "";
}