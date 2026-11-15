namespace TacticalRmm.Domain.Entities;

public abstract class AuditableEntity
{
    public string? CreatedBy { get; set; }
    public DateTime? CreatedTime { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedTime { get; set; }
}

public class Client : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Site> Sites { get; set; } = new();
    /// <summary>Policy applied to every agent of this client (unless the site or the agent has its own).</summary>
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
}

public class Site : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public List<Agent> Agents { get; set; } = new();
    /// <summary>Policy applied to every agent of this site (unless the agent has its own).</summary>
    public int? PolicyId { get; set; }
    public Policy? Policy { get; set; }
}

public class Policy : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Desc { get; set; }
    public bool Active { get; set; }
    public bool Enforced { get; set; }
}
