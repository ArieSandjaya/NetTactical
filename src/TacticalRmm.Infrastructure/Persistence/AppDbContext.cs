using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<AgentToken> AgentTokens => Set<AgentToken>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentHistory> AgentHistory => Set<AgentHistory>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<AutomatedTask> AutomatedTasks => Set<AutomatedTask>();
    public DbSet<TaskResult> TaskResults => Set<TaskResult>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<CheckMetric> CheckMetrics => Set<CheckMetric>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<WinUpdate> WinUpdates => Set<WinUpdate>();
    public DbSet<WinUpdatePolicy> WinUpdatePolicies => Set<WinUpdatePolicy>();
    public DbSet<InstalledSoftware> InstalledSoftware => Set<InstalledSoftware>();
    public DbSet<AgentNote> AgentNotes => Set<AgentNote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Agent>(e =>
        {
            e.HasIndex(x => x.AgentId).IsUnique();
            e.HasIndex(x => x.MonitoringType);
            e.Property(x => x.Services).HasColumnType("jsonb");
            e.Property(x => x.Disks).HasColumnType("jsonb");
            e.HasOne(x => x.Site).WithMany(s => s.Agents).HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.AgentId).IsUnique();
            e.HasOne(x => x.Agent).WithOne().HasForeignKey<User>(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Role>(e =>
        {
            e.HasMany(x => x.CanViewClients).WithMany();
            e.HasMany(x => x.CanViewSites).WithMany();
        });
        b.Entity<UserToken>(e => { e.HasIndex(x => x.TokenKey); e.HasIndex(x => x.Digest).IsUnique(); });
        b.Entity<AgentToken>(e => { e.HasKey(x => x.Key); e.HasIndex(x => x.UserId).IsUnique(); });
        b.Entity<ApiKey>().HasIndex(x => x.Key).IsUnique();
        b.Entity<Check>(e =>
        {
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Script).WithMany().HasForeignKey(x => x.ScriptId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CheckResult>(e =>
        {
            e.HasIndex(x => new { x.AgentId, x.CheckId }).IsUnique();
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Check).WithMany().HasForeignKey(x => x.CheckId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AutomatedTask>(e =>
        {
            e.Property(x => x.Actions).HasColumnType("jsonb");
            e.HasIndex(x => x.WinTaskName).IsUnique();
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedCheck).WithMany().HasForeignKey(x => x.AssignedCheckId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TaskResult>(e =>
        {
            e.HasIndex(x => new { x.AgentId, x.TaskId }).IsUnique();
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AgentHistory>().Property(x => x.ScriptResults).HasColumnType("jsonb");
        b.Entity<WinUpdate>(e =>
        {
            e.HasIndex(x => new { x.AgentId, x.Guid });
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WinUpdatePolicy>(e => { e.HasIndex(x => x.AgentId); e.HasIndex(x => x.PolicyId); });
        b.Entity<InstalledSoftware>(e =>
        {
            e.Property(x => x.Software).HasColumnType("jsonb");
            e.HasIndex(x => x.AgentId).IsUnique();
        });
        b.Entity<AgentNote>().HasIndex(x => x.AgentId);
        b.Entity<CheckMetric>(e =>
        {
            e.HasIndex(x => new { x.AgentId, x.CheckType, x.Time });
            e.HasIndex(x => x.Time);
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.AgentId, x.Resolved });
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AuditLog>(e => { e.Property(x => x.BeforeValue).HasColumnType("jsonb"); e.Property(x => x.AfterValue).HasColumnType("jsonb"); e.Property(x => x.DebugInfo).HasColumnType("jsonb"); });

        // snake_case schema. The agents_agent table + its columns MUST match Django because
        // the Go nats-api service (natsapi/svc.go) writes to that table via raw SQL.
        foreach (var et in b.Model.GetEntityTypes())
        {
            et.SetTableName(et.ClrType == typeof(Agent) ? "agents_agent" : Snake(et.GetTableName() ?? et.ClrType.Name));
            foreach (var p in et.GetProperties())
                p.SetColumnName(Snake(p.GetColumnName()));
        }
    }

    private static string Snake(string s) => Regex.Replace(s, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var e in ChangeTracker.Entries<AuditableEntity>())
        {
            if (e.State == EntityState.Added) { e.Entity.CreatedTime ??= now; e.Entity.ModifiedTime = now; }
            else if (e.State == EntityState.Modified) e.Entity.ModifiedTime = now;
        }
        return base.SaveChangesAsync(ct);
    }
}
