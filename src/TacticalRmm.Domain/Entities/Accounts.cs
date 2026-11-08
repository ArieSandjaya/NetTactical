namespace TacticalRmm.Domain.Entities;

public class Role : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsSuperuser { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Client> CanViewClients { get; set; } = new();
    public List<Site> CanViewSites { get; set; } = new();
}

public class User : AuditableEntity
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsSuperuser { get; set; }
    public bool BlockDashboardLogin { get; set; }
    public bool IsInstallerUser { get; set; }
    public string? TotpKey { get; set; }
    public string? LastLoginIp { get; set; }
    public int? RoleId { get; set; }
    public Role? Role { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
}

/// <summary>Equivalent of knox_authtoken (user) and authtoken_token (agent). Only the SHA-512 digest is stored.</summary>
public class UserToken
{
    public long Id { get; set; }
    public string Digest { get; set; } = "";
    public string TokenKey { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime Created { get; set; }
    public DateTime? Expiry { get; set; }
}

public class ApiKey : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime? Expiration { get; set; }
}

/// <summary>
/// The agent token is stored as-is (not hashed) because it is used as the NATS password in nats-rmm.conf
/// (same as the DRF authtoken in Django). Dashboard tokens use the digest-based <see cref="UserToken"/>.
/// </summary>
public class AgentToken
{
    public string Key { get; set; } = "";
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime Created { get; set; }
}
