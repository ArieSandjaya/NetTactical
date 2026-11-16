using System.Text;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class ReportPostgresTests
{
    private static readonly User Admin = new() { Username = "admin", IsSuperuser = true };

    private static async Task<(TacticalRmm.Infrastructure.Persistence.AppDbContext db, ReportService svc, Client c1, Client c2)> SeedAsync()
    {
        var db = await PgDb.CreateAsync();
        var c1 = new Client { Name = "One" }; var c2 = new Client { Name = "Two" };
        var s1 = new Site { Name = "S1", Client = c1 }; var s2 = new Site { Name = "S2", Client = c2 };
        var a1 = new Agent { AgentId = "a1", Hostname = "=HOST1", Site = s1 }; var a2 = new Agent { AgentId = "a2", Hostname = "HOST2", Site = s2 };
        var now = DateTime.UtcNow;
        db.AddRange(c1, c2, s1, s2, a1, a2,
            new Alert { Agent = a1, AlertTime = now.AddDays(-1), Severity = AlertSeverity.Error, Message = "disk, full", AlertType = AlertType.Check },
            new Alert { Agent = a2, AlertTime = now.AddDays(-2), Severity = AlertSeverity.Warning, Message = "overdue", Resolved = true, ResolvedOn = now.AddDays(-1) },
            new Alert { Agent = a1, AlertTime = now.AddDays(-90), Severity = AlertSeverity.Info, Message = "too old" },
            new Alert { Agent = a1, AlertTime = now.AddDays(-1), Severity = AlertSeverity.Info, Message = "hidden one", Hidden = true });
        await db.SaveChangesAsync();
        return (db, new ReportService(db, null!), c1, c2);
    }

    [PostgresFact]
    public async Task AlertReportKeepsThePeriodAndSkipsHiddenAlerts()
    {
        var (db, svc, _, _) = await SeedAsync();
        await using var _db = db;
        var r = (await svc.BuildAsync(Admin, "alerts", null, null, null, null)).Value!;
        Assert.Equal(new[] { "disk, full", "overdue" }, r.Rows.Select(x => x[6]).ToArray());
        var wide = (await svc.BuildAsync(Admin, "alerts", null, null, DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd"), null)).Value!;
        Assert.Contains("too old", wide.Rows.Select(x => x[6]));
        Assert.DoesNotContain("hidden one", wide.Rows.Select(x => x[6]));
    }

    [PostgresFact]
    public async Task AlertReportCanBeFilteredToAClient()
    {
        var (db, svc, c1, c2) = await SeedAsync();
        await using var _db = db;
        var one = (await svc.BuildAsync(Admin, "alerts", c1.Id, null, null, null)).Value!;
        Assert.Equal(new[] { "disk, full" }, one.Rows.Select(x => x[6]).ToArray());
        var two = (await svc.BuildAsync(Admin, "alerts", c2.Id, null, null, null)).Value!;
        Assert.Equal(new[] { "overdue" }, two.Rows.Select(x => x[6]).ToArray());
    }

    [PostgresFact]
    public async Task ARestrictedRoleOnlySeesItsOwnClientAndNeedsThePermission()
    {
        var (db, svc, c1, c2) = await SeedAsync();
        await using var _db = db;
        var role = new Role { Name = "r", Permissions = [Perm.ListAlerts] };
        role.CanViewClients.Add(c2);
        var limited = new User { Username = "limited", Role = role };
        var r = (await svc.BuildAsync(limited, "alerts", null, null, null, null)).Value!;
        Assert.Equal(new[] { "overdue" }, r.Rows.Select(x => x[6]).ToArray());
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.BuildAsync(limited, "assets", null, null, null, null)).Status);
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.BuildAsync(new User { Username = "none" }, "alerts", null, null, null, null)).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.BuildAsync(Admin, "nope", null, null, null, null)).Status);
        Assert.Equal(new[] { "alerts" }, svc.Available(limited).Select(x => x.Name).ToArray());
    }

    [PostgresFact]
    public async Task CsvIsUtf8WithBomAndAgentTextCannotBecomeAFormula()
    {
        var (db, svc, _, _) = await SeedAsync();
        await using var _db = db;
        var f = (await svc.CsvAsync(Admin, "alerts", null, null, null, null)).Value!;
        Assert.StartsWith("nettactical-alerts-", f.FileName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, f.Content.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(f.Content, 3, f.Content.Length - 3);
        Assert.StartsWith("Time (UTC),Severity,Type,Client,Site,Hostname,Message,Status,Resolved (UTC)\r\n", text);
        Assert.Contains("'=HOST1", text);
        Assert.Contains("\"disk, full\"", text);
        Assert.DoesNotContain(",=HOST1", text);
    }

    [PostgresFact]
    public async Task BadPeriodIsRefused()
    {
        var (db, svc, _, _) = await SeedAsync();
        await using var _db = db;
        Assert.Equal(OutcomeStatus.Invalid, (await svc.BuildAsync(Admin, "alerts", null, null, "2026-13-45", null)).Status);
    }
}
