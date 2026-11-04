using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
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
    private readonly AccountCleanupService _accountCleanup;

    public AuthController(
        UserManager<ApplicationUser> users,
        JwtTokenService jwtTokens,
        OtpAuthService otpAuth,
        AccountCleanupService accountCleanup)
    {
        _users = users;
        _jwtTokens = jwtTokens;
        _otpAuth = otpAuth;
        _accountCleanup = accountCleanup;
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

    [HttpPost("password/reset")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] PasswordResetRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Email không hợp lệ." });

        var otpResult = await _otpAuth.VerifyAsync(email, request.Otp ?? "");
        if (!otpResult.Succeeded)
        {
            if (otpResult.Expired)
                return BadRequest(new { message = "Mã OTP đã hết hạn. Vui lòng yêu cầu mã mới." });
            if (otpResult.Locked)
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = "Bạn đã nhập sai quá nhiều lần. Vui lòng yêu cầu mã OTP mới."
                });
            return Unauthorized(new { message = "Mã OTP không đúng." });
        }

        var user = await _users.FindByEmailAsync(email);
        if (user == null)
        {
            return Ok(new
            {
                message = "Nếu tài khoản tồn tại, mật khẩu đã được đặt lại."
            });
        }

        if (await _users.HasPasswordAsync(user))
        {
            var removeResult = await _users.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Không thể đặt lại mật khẩu.",
                    errors = removeResult.Errors.Select(x => x.Description).ToArray()
                });
            }
        }

        var addResult = await _users.AddPasswordAsync(user, request.NewPassword ?? "");
        if (!addResult.Succeeded)
        {
            return BadRequest(new
            {
                message = "Không thể đặt lại mật khẩu.",
                errors = addResult.Errors.Select(x => x.Description).ToArray()
            });
        }

        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        user.EmailConfirmed = true;
        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BadRequest(new
            {
                message = "Không thể cập nhật tài khoản.",
                errors = updateResult.Errors.Select(x => x.Description).ToArray()
            });
        }

        return Ok(new { message = "Mật khẩu đã được đặt lại." });
    }

    [HttpPost("password/change")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });

        var user = await _users.FindByIdAsync(userId);
        if (user == null)
            return Unauthorized(new { message = "Không tìm thấy tài khoản." });

        IdentityResult result;
        if (!await _users.HasPasswordAsync(user))
        {
            result = await _users.AddPasswordAsync(user, request.NewPassword ?? "");
        }
        else if (await _users.CheckPasswordAsync(user, request.CurrentPassword ?? ""))
        {
            result = await _users.ChangePasswordAsync(
                user,
                request.CurrentPassword ?? "",
                request.NewPassword ?? "");
        }
        else
        {
            return BadRequest(new { message = "Mật khẩu hiện tại không đúng." });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Không thể đổi mật khẩu.",
                errors = result.Errors.Select(x => x.Description).ToArray()
            });
        }

        return Ok(new { message = "Mật khẩu đã được đổi." });
    }

    [HttpDelete("account")]
    [Authorize]
    public async Task<IActionResult> DeleteAccount([FromBody] AccountDeleteRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ." });

        var user = await _users.FindByIdAsync(userId);
        if (user == null)
            return Unauthorized(new { message = "Không tìm thấy tài khoản." });

        if (await _users.HasPasswordAsync(user))
        {
            var passwordValid = await _users.CheckPasswordAsync(user, request.Password ?? "");
            if (!passwordValid)
                return BadRequest(new { message = "Mật khẩu không đúng." });
        }

        if (await _accountCleanup.HasOwnedFamilyWithOtherMembersAsync(userId))
        {
            return StatusCode(StatusCodes.Status409Conflict, new
            {
                message = "Bạn là chủ nhóm gia đình, hãy xóa nhóm hoặc xóa các thành viên trước."
            });
        }

        try
        {
            await _accountCleanup.CleanupAsync(user);
            return Ok(new { message = "Đã xóa tài khoản." });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không thể xóa tài khoản lúc này. Vui lòng thử lại sau."
            });
        }
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

public class PasswordResetRequest
{
    public string Email { get; set; } = "";
    public string Otp { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class PasswordChangeRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public class AccountDeleteRequest
{
    public string Password { get; set; } = "";
}