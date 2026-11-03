using Microsoft.AspNetCore.Identity;

namespace SmartMoney.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public class FamilyGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string OwnerUserId { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<FamilyMember> Members { get; set; } = new();
}

public class FamilyMember
{
    public int Id { get; set; }
    public int FamilyGroupId { get; set; }
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Relationship { get; set; } = "Khác";
    public string AccessRole { get; set; } = "Member";
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
    public FamilyGroup? FamilyGroup { get; set; }
}

public class Wallet
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "cash";
    public long InitialBalance { get; set; }
    public string? ClientId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class FamilyTransaction
{
    public int Id { get; set; }
    public string EventId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int? FamilyGroupId { get; set; }
    public int? WalletId { get; set; }
    public int? ToWalletId { get; set; }
    public long Amount { get; set; }
    public string Type { get; set; } = "expense";
    public string? AdjustmentDirection { get; set; }
    public string CategoryName { get; set; } = "Khác";
    public string Source { get; set; } = "";
    public string BankName { get; set; } = "";
    public string PackageName { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = "";
}

public class FamilyBudget
{
    public int Id { get; set; }
    public int FamilyGroupId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public long MonthlyLimit { get; set; }
    public bool Warning80Sent { get; set; }
    public bool Exceeded100Sent { get; set; }
}

public class DeviceToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}