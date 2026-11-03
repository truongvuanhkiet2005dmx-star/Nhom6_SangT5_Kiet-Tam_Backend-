using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Services;

public class JwtTokenService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IConfiguration _configuration;

    public JwtTokenService(
        UserManager<ApplicationUser> users,
        IConfiguration configuration)
    {
        _users = users;
        _configuration = configuration;
    }

    public async Task<object> CreateResponseAsync(ApplicationUser user)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Thiếu cấu hình Jwt:Key.");
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];
        var roles = await _users.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email ?? "")
        };
        claims.AddRange(roles.Select(role =>
            new Claim(ClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            Expires = DateTime.UtcNow.AddHours(2),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new
        {
            accessToken = token,
            tokenType = "Bearer",
            expiresIn = 7200,
            user = new
            {
                id = user.Id,
                email = user.Email,
                displayName = user.DisplayName
            }
        };
    }
}
