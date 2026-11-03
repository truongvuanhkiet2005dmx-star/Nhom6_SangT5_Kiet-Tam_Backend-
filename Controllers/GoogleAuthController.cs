using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? email
                    : displayName
            };

            var createResult = await _userManager.CreateAsync(user);

            if (!createResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Không tạo được tài khoản.",
                    errors = createResult.Errors.Select(e => e.Description)
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