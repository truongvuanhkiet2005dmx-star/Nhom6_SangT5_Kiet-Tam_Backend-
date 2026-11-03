using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class GoogleAuthController : ControllerBase
{
    private readonly IServiceProvider _services;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public GoogleAuthController(
        IServiceProvider services,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        _services = services;
        _userManager = userManager;
        _configuration = configuration;
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

        var displayName =
            decodedToken.Claims.TryGetValue("name", out var nameValue)
                ? nameValue?.ToString()
                : null;

        if (string.IsNullOrWhiteSpace(email))
            return Unauthorized("Tài khoản Firebase không có email.");

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

        var key = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(500, "Backend chưa cấu hình Jwt:Key.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? email),
            new Claim(ClaimTypes.Name, user.DisplayName)
        };

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key));

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        return Ok(new
        {
            accessToken = new JwtSecurityTokenHandler().WriteToken(jwt),
            user = new
            {
                user.Id,
                user.Email,
                user.DisplayName
            }
        });
    }
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = "";
}