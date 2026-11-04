using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
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
                IsOwner = g.OwnerUserId == userId,
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
        if (request.Name.Trim().Length > 100)
            return BadRequest("Tên nhóm không được vượt quá 100 ký tự.");
        if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Trim().Length > 100)
            return BadRequest("Tên hiển thị không được vượt quá 100 ký tự.");

        var family = new FamilyGroup
        {
            Name = request.Name.Trim(),
            OwnerUserId = userId,
            InviteCode = "",
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
        for (var attempt = 0; attempt < 5; attempt++)
        {
            family.InviteCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException)
            {
                if (attempt == 4)
                    return Conflict("Không thể tạo mã mời duy nhất sau 5 lần thử.");
            }
        }

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
    [EnableRateLimiting("join")]
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
        if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Trim().Length > 100)
            return BadRequest("Tên hiển thị không được vượt quá 100 ký tự.");
        if (!string.IsNullOrWhiteSpace(request.Relationship) && request.Relationship.Trim().Length > 50)
            return BadRequest("Quan hệ không được vượt quá 50 ký tự.");

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
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.Entry(member).State = EntityState.Detached;

            var concurrentMembership = await _db.FamilyMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == userId);

            if (concurrentMembership != null)
            {
                return Conflict("Tài khoản này đã thuộc một nhóm gia đình.");
            }

            throw;
        }

        return Ok(new
        {
            message = "Tham gia nhóm thành công.",
            family.Id,
            family.Name
        });
    }

    // Thành viên tự rời nhóm
    [HttpPost("{familyGroupId:int}/leave")]
    public async Task<IActionResult> Leave(int familyGroupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var group = await _db.FamilyGroups
            .FirstOrDefaultAsync(g => g.Id == familyGroupId);

        if (group == null)
            return NotFound("Không tìm thấy nhóm gia đình.");

        var member = await _db.FamilyMembers.FirstOrDefaultAsync(m =>
            m.FamilyGroupId == familyGroupId && m.UserId == userId);

        if (member == null)
            return Forbid();

        var memberCount = await _db.FamilyMembers.CountAsync(m =>
            m.FamilyGroupId == familyGroupId);

        if (string.Equals(member.AccessRole, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            if (memberCount > 1)
            {
                return Conflict(
                    "Chủ nhóm không thể tự rời khi nhóm còn thành viên khác.");
            }

            return Conflict(
                "Bạn là chủ nhóm. Hãy dùng DELETE /api/families/{familyGroupId} để xóa nhóm.");
        }

        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đã rời nhóm gia đình." });
    }

    // Chủ nhóm xóa thành viên khác
    [HttpDelete("{familyGroupId:int}/members/{memberUserId}")]
    public async Task<IActionResult> RemoveMember(
        int familyGroupId,
        string memberUserId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var owner = await _db.FamilyMembers.FirstOrDefaultAsync(m =>
            m.FamilyGroupId == familyGroupId &&
            m.UserId == userId);

        if (owner == null)
            return Forbid();

        if (!string.Equals(owner.AccessRole, "Owner", StringComparison.OrdinalIgnoreCase))
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "Chỉ chủ nhóm mới được xóa thành viên." });

        if (string.IsNullOrWhiteSpace(memberUserId))
            return BadRequest("Thiếu mã thành viên cần xóa.");

        if (memberUserId == userId)
            return BadRequest("Chủ nhóm không thể tự xóa bằng API này. Hãy sử dụng chức năng rời nhóm.");

        var member = await _db.FamilyMembers.FirstOrDefaultAsync(m =>
            m.FamilyGroupId == familyGroupId &&
            m.UserId == memberUserId);

        if (member == null)
            return NotFound("Không tìm thấy thành viên trong nhóm.");

        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Đã xóa thành viên khỏi nhóm." });
    }

    // Chủ nhóm xóa nhóm gia đình; giữ lại lịch sử giao dịch bằng cách bỏ liên kết nhóm
    [HttpDelete("{familyGroupId:int}")]
    public async Task<IActionResult> DeleteFamily(int familyGroupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var group = await _db.FamilyGroups.FirstOrDefaultAsync(g =>
            g.Id == familyGroupId);

        if (group == null)
            return NotFound("Không tìm thấy nhóm gia đình.");

        if (group.OwnerUserId != userId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { message = "Chỉ chủ nhóm mới được xóa nhóm gia đình." });
        }

        var ownerMember = await _db.FamilyMembers.FirstOrDefaultAsync(m =>
            m.FamilyGroupId == familyGroupId &&
            m.UserId == userId);

        if (ownerMember == null ||
            !string.Equals(ownerMember.AccessRole, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(
                "Không thể xác nhận quyền chủ nhóm trong cơ sở dữ liệu.");
        }

        var transactions = await _db.FamilyTransactions
            .Where(t => t.FamilyGroupId == familyGroupId)
            .ToListAsync();

        foreach (var transaction in transactions)
            transaction.FamilyGroupId = null;

        _db.FamilyGroups.Remove(group);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Đã xóa nhóm gia đình. Lịch sử giao dịch được giữ lại.",
            familyGroupId
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