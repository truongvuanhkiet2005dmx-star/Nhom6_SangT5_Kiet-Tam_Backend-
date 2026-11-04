using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMoney.Api.Models;
using SmartMoney.Api.Services;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtTokenService _jwtTokens;
    private readonly OtpAuthService _otpAuth;

    public AuthController(
        UserManager<ApplicationUser> users,
        JwtTokenService jwtTokens,
        OtpAuthService otpAuth)
    {
        _users = users;
        _jwtTokens = jwtTokens;
        _otpAuth = otpAuth;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var displayName = request.DisplayName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(displayName))
            return BadRequest("Tên hiển thị không được để trống.");
        if (displayName.Length > 100)
            return BadRequest("Tên hiển thị không được vượt quá 100 ký tự.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DisplayName = displayName,
            LockoutEnabled = true
        };

        var result = await _users.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            var errors = result.Errors.Select(x => x.Description).ToList();
            return BadRequest(new
            {
                message = "Đăng ký thất bại. " + string.Join(" ", errors),
                errors
            });
        }

        return Ok(await CreateResponse(user));
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.FindByEmailAsync(email);

        if (user is null)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });
        }

        if (await _users.IsLockedOutAsync(user))
        {
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần."
            });
        }

        var signIn = HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
        var signInResult = await signIn.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, new
            {
                message = "Tài khoản tạm thời bị khóa do nhập sai mật khẩu nhiều lần."
            });
        }

        if (!signInResult.Succeeded)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });
        }

        await _users.ResetAccessFailedCountAsync(user);

        return Ok(await CreateResponse(user));
    }

    [HttpPost("otp/request")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RequestOtp([FromBody] OtpRequestRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email không hợp lệ.");

        var result = await _otpAuth.RequestAsync(email);
        if (result == OtpRequestResult.RetryLater)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Vui lòng chờ trước khi gửi lại mã OTP."
            });
        }
        if (result == OtpRequestResult.DeliveryFailed)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Không thể gửi mã OTP lúc này. Vui lòng thử lại sau."
            });
        }

        return Ok(new
        {
            message = "Nếu email hợp lệ, mã OTP đã được gửi.",
            expiresInSeconds = 300
        });
    }

    [HttpPost("otp/verify")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("Email không hợp lệ.");

        var result = await _otpAuth.VerifyAsync(email, request.Otp ?? "");
        if (!result.Succeeded)
        {
            if (result.Expired)
                return BadRequest(new { message = "Mã OTP đã hết hạn. Vui lòng yêu cầu mã mới." });
            if (result.Locked)
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = "Bạn đã nhập sai quá nhiều lần. Vui lòng yêu cầu mã OTP mới."
                });
            return Unauthorized(new { message = "Mã OTP không đúng." });
        }

        var user = await _users.FindByEmailAsync(email);
        if (user == null)
        {
            var emailLocalPart = email.Split('@')[0].Trim('.');
            var displayName = string.IsNullOrWhiteSpace(emailLocalPart)
                ? "Người dùng"
                : emailLocalPart[..Math.Min(emailLocalPart.Length, 100)];

            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                NormalizedEmail = _users.NormalizeEmail(email),
                NormalizedUserName = _users.NormalizeName(email),
                EmailConfirmed = true,
                DisplayName = displayName,
                LockoutEnabled = true
            };

            var createResult = await _users.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                user = await _users.FindByEmailAsync(email);
                if (user == null)
                {
                    return BadRequest(new
                    {
                        message = "Không thể tạo tài khoản.",
                        errors = createResult.Errors.Select(x => x.Description).ToArray()
                    });
                }
            }
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updateResult = await _users.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return BadRequest(new { message = "Không thể xác nhận email." });
        }

        return Ok(await CreateResponse(user));
    }

    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return NotFound();

        return Ok(new
        {
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName
        });
    }

    private async Task<object> CreateResponse(ApplicationUser user)
    {
        return await _jwtTokens.CreateResponseAsync(user);
    }

    private static string NormalizeEmail(string? email)
    {
        return email?.Trim().ToLowerInvariant() ?? "";
    }
}

public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, MinLength(6)]
    public string Password { get; set; } = "";

    [Required]
    public string DisplayName { get; set; } = "";
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public class OtpRequestRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}

public class OtpVerifyRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Otp { get; set; } = "";
}