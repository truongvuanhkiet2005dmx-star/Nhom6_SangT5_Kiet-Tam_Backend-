using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartMoney.Api.Data;
using SmartMoney.Api.Models;

namespace SmartMoney.Api.Controllers;

[ApiController]
[Route("api/families")]
[Authorize]
public class FamiliesController : ControllerBase
{
    private readonly AppDbContext _db;

    public FamiliesController(AppDbContext db)
    {
        _db = db;
    }

    // Xem nhóm gia đình và danh sách thành viên
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var families = await _db.FamilyGroups
            .Where(g => g.Members.Any(m => m.UserId == userId))
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.InviteCode,
                g.CreatedAtUtc,
                Members = g.Members.Select(m => new
                {
                    m.UserId,
                    m.DisplayName,
                    m.Relationship,
                    m.AccessRole,
                    m.JoinedAtUtc
                })
            })
            .ToListAsync();

        return Ok(families);
    }

    // Tạo nhóm gia đình; người tạo tự động là chủ nhóm
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFamilyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var alreadyInFamily = await _db.FamilyMembers
            .AnyAsync(m => m.UserId == userId);

        if (alreadyInFamily)
            return Conflict("Tài khoản này đã thuộc một nhóm gia đình.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Tên nhóm không được để trống.");

        var family = new FamilyGroup
        {
            Name = request.Name.Trim(),
            OwnerUserId = userId,
            InviteCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            CreatedAtUtc = DateTime.UtcNow,
            Members =
            [
                new FamilyMember
                {
                    UserId = userId,
                    DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                        ? "Chủ nhóm"
                        : request.DisplayName.Trim(),
                    Relationship = "Chủ nhóm",
                    AccessRole = "Owner",
                    JoinedAtUtc = DateTime.UtcNow
                }
            ]
        };

        _db.FamilyGroups.Add(family);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            family.Id,
            family.Name,
            family.InviteCode,
            family.CreatedAtUtc
        });
    }

    // Tham gia nhóm bằng mã mời
    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinFamilyRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var alreadyInFamily = await _db.FamilyMembers
            .AnyAsync(m => m.UserId == userId);

        if (alreadyInFamily)
            return Conflict("Tài khoản này đã thuộc một nhóm gia đình.");

        var inviteCode = request.InviteCode?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(inviteCode))
            return BadRequest("Bạn cần nhập mã mời.");

        var family = await _db.FamilyGroups
            .FirstOrDefaultAsync(g => g.InviteCode == inviteCode);

        if (family == null)
            return NotFound("Mã mời không tồn tại.");

        var member = new FamilyMember
        {
            FamilyGroupId = family.Id,
            UserId = userId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "Thành viên"
                : request.DisplayName.Trim(),
            Relationship = string.IsNullOrWhiteSpace(request.Relationship)
                ? "Khác"
                : request.Relationship.Trim(),
            AccessRole = "Member",
            JoinedAtUtc = DateTime.UtcNow
        };

        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Tham gia nhóm thành công.",
            family.Id,
            family.Name
        });
    }
}

public class CreateFamilyRequest
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
}

public class JoinFamilyRequest
{
    public string? InviteCode { get; set; }
    public string? DisplayName { get; set; }
    public string? Relationship { get; set; }
}