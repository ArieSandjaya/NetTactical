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
}

public class Site : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public List<Agent> Agents { get; set; } = new();
}

public class Policy : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Desc { get; set; }
    public bool Active { get; set; }
    public bool Enforced { get; set; }
}
