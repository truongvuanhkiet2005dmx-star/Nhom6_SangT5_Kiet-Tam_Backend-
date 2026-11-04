using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Services;

public enum OtpRequestResult
{
    Sent,
    RetryLater,
    DeliveryFailed
}

public sealed record OtpVerifyResult(bool Succeeded, bool InvalidOtp, bool Expired, bool Locked);

public class OtpAuthService
{
    private const int OtpLifetimeSeconds = 300;
    private const int ResendCooldownSeconds = 60;
    private const int MaxFailedAttempts = 5;

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OtpAuthService> _logger;

    public OtpAuthService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<OtpAuthService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<OtpRequestResult> RequestAsync(string normalizedEmail)
    {
        var now = DateTime.UtcNow;
        var challenge = await _db.EmailOtpChallenges.FirstOrDefaultAsync(
            c => c.Email == normalizedEmail);

        if (challenge != null &&
            challenge.LastSentAtUtc.AddSeconds(ResendCooldownSeconds) > now &&
            challenge.UsedAtUtc == null)
        {
            return OtpRequestResult.RetryLater;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
            .ToString("D6", CultureInfo.InvariantCulture);
        var saltBytes = RandomNumberGenerator.GetBytes(32);
        var salt = Convert.ToBase64String(saltBytes);

        if (challenge == null)
        {
            challenge = new EmailOtpChallenge { Email = normalizedEmail };
            _db.EmailOtpChallenges.Add(challenge);
        }

        challenge.CodeHash = HashOtp(code, saltBytes);
        challenge.CodeSalt = salt;
        challenge.CreatedAtUtc = now;
        challenge.LastSentAtUtc = now;
        challenge.ExpiresAtUtc = now.AddSeconds(OtpLifetimeSeconds);
        challenge.FailedAttempts = 0;
        challenge.UsedAtUtc = null;

        await _db.SaveChangesAsync();

        try
        {
            await SendEmailAsync(normalizedEmail, code);
            return OtpRequestResult.Sent;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Không thể gửi mã OTP đến email người dùng.");
            challenge.LastSentAtUtc = now.AddSeconds(-ResendCooldownSeconds);
            await _db.SaveChangesAsync();
            return OtpRequestResult.DeliveryFailed;
        }
    }

    public async Task<OtpVerifyResult> VerifyAsync(string normalizedEmail, string otp)
    {
        var now = DateTime.UtcNow;
        var challenge = await _db.EmailOtpChallenges.FirstOrDefaultAsync(
            c => c.Email == normalizedEmail);

        if (challenge == null)
            return new OtpVerifyResult(false, true, false, false);

        if (challenge.UsedAtUtc.HasValue)
            return new OtpVerifyResult(false, true, false, false);

        if (challenge.FailedAttempts >= MaxFailedAttempts)
            return new OtpVerifyResult(false, false, false, true);

        if (challenge.ExpiresAtUtc <= now)
            return new OtpVerifyResult(false, false, true, false);

        var supplied = otp.Trim();
        if (!IsSixDigitOtp(supplied))
        {
            await RecordFailureAsync(challenge, now);
            return new OtpVerifyResult(false, true, false,
                challenge.FailedAttempts >= MaxFailedAttempts);
        }

        var saltBytes = Convert.FromBase64String(challenge.CodeSalt);
        var suppliedHash = HashOtp(supplied, saltBytes);
        var valid = CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(challenge.CodeHash),
            Convert.FromBase64String(suppliedHash));

        if (!valid)
        {
            await RecordFailureAsync(challenge, now);
            return new OtpVerifyResult(false, true, false,
                challenge.FailedAttempts >= MaxFailedAttempts);
        }

        challenge.UsedAtUtc = now;
        await _db.SaveChangesAsync();
        return new OtpVerifyResult(true, false, false, false);
    }

    private async Task RecordFailureAsync(EmailOtpChallenge challenge, DateTime now)
    {
        challenge.FailedAttempts++;
        if (challenge.FailedAttempts >= MaxFailedAttempts)
            challenge.ExpiresAtUtc = now;
        await _db.SaveChangesAsync();
    }

    private static bool IsSixDigitOtp(string otp)
    {
        if (otp.Length != 6)
            return false;
        foreach (var character in otp)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }
        return true;
    }

    private static string HashOtp(string otp, byte[] salt)
    {
        var otpBytes = Encoding.UTF8.GetBytes(otp);
        var input = new byte[salt.Length + otpBytes.Length];
        salt.CopyTo(input, 0);
        otpBytes.CopyTo(input, salt.Length);
        return Convert.ToBase64String(SHA256.HashData(input));
    }

    private async Task SendEmailAsync(string email, string code)
    {
        var host = _configuration["Smtp:Host"];
        var port = _configuration.GetValue("Smtp:Port", 587);
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var fromEmail = _configuration["Smtp:FromEmail"];
        var fromName = _configuration["Smtp:FromName"] ?? "SmartMoney";
        var enableSsl = _configuration.GetValue("Smtp:EnableSsl", true);

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(fromEmail))
        {
            throw new InvalidOperationException(
                "Thiếu cấu hình SMTP: Smtp:Host hoặc Smtp:FromEmail.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = "Mã đăng nhập SmartMoney",
            Body =
                $"Mã OTP của bạn là {code}. Mã hết hạn sau 5 phút và chỉ dùng một lần.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(email));

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        await client.SendMailAsync(message);
    }
}
