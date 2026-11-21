using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class DeploymentPostgresTests
{
    private sealed class Tokens : ITokenService
    {
        public int Issued;
        public Task<(string RawToken, DateTime Expiry)> CreateInstallerTokenAsync(int installerUserId, TimeSpan lifetime, CancellationToken ct = default) { Issued++; return Task.FromResult((new string('a', 64), DateTime.UtcNow.Add(lifetime))); }
        public Task<(string RawToken, DateTime Expiry)> CreateUserTokenAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> CreateAgentTokenAsync(int userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<User?> ValidateAsync(string rawToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(string rawToken, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }

    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };

    private static async Task<(DeploymentService svc, TacticalRmm.Infrastructure.Persistence.AppDbContext db, Tokens tokens, Audit audit, Site site)> SetupAsync()
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "Acme" }; var s = new Site { Name = "Main", Client = c };
        db.AddRange(c, s, new User { Username = "installer", IsInstallerUser = true, IsActive = true, PasswordHash = "x" });
        await db.SaveChangesAsync();
        var tokens = new Tokens(); var audit = new Audit();
        return (new DeploymentService(db, tokens, audit, Options.Create(new TacticalOptions { LatestAgentVer = "2.12.0" })), db, tokens, audit, s);
    }

    private static DeploymentRequest Req(Site s, int? max = null) => new() { ClientId = s.ClientId, SiteId = s.Id, MaxUses = max, ExpiresHours = 24 };

    [PostgresFact]
    public async Task ALinkServesAnInstallScriptWithAFreshTokenEachTime()
    {
        var (svc, db, tokens, audit, site) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, Req(site), "https://rmm.example.com")).Value!;
        Assert.StartsWith("https://rmm.example.com/deploy/", d.Link);
        var uid = d.Link.Split('/').Last();
        var s1 = (await svc.ScriptAsync(uid)).Value!;
        Assert.Contains("tacticalagent-v2.12.0-windows-amd64.exe", s1);
        Assert.Contains($"--client-id {site.ClientId} --site-id {site.Id}", s1);
        Assert.Contains(new string('a', 64), s1);
        await svc.ScriptAsync(uid);
        Assert.Equal(2, tokens.Issued);
        Assert.Equal(2, (await svc.ListAsync(Admin)).Value!.Single().Uses);
        Assert.Contains(audit.Messages, m => m.Contains("was opened") && m.Contains("Acme / Main"));
    }

    [PostgresFact]
    public async Task TheUseLimitExpiryAndUnknownLinksAreAllRefusedTheSameWay()
    {
        var (svc, db, tokens, _, site) = await SetupAsync(); await using var _ = db;
        var limited = (await svc.CreateAsync(Admin, Req(site, max: 1), "https://rmm.example.com")).Value!.Link.Split('/').Last();
        Assert.True((await svc.ScriptAsync(limited)).IsOk);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ScriptAsync(limited)).Status);              // used up

        var expiring = (await svc.CreateAsync(Admin, Req(site), "https://rmm.example.com")).Value!.Link.Split('/').Last();
        await db.Deployments.Where(x => x.Uid == expiring).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        var issued = tokens.Issued;
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ScriptAsync(expiring)).Status);              // expired
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ScriptAsync(new string('0', 32))).Status);   // unknown
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ScriptAsync("../etc/passwd")).Status);       // not even a uid
        Assert.Equal(issued, tokens.Issued);                                                         // no token was made for a refused link
    }

    [PostgresFact]
    public async Task OnlyAUserWhoSeesTheSiteCanMakeOrDeleteALink()
    {
        var (svc, db, _, _, site) = await SetupAsync(); await using var _ = db;
        var other = new Site { Name = "Other", Client = new Client { Name = "Other client" } };
        db.Add(other); await db.SaveChangesAsync();
        var scoped = new User { Id = 2, Username = "scoped", Role = new Role { Name = "r", Permissions = ["can_manage_deployments"], CanViewSites = [site] } };
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.CreateAsync(scoped, Req(other), "https://rmm.example.com")).Status);
        var mine = (await svc.CreateAsync(scoped, Req(site), "https://rmm.example.com")).Value!;
        var theirs = (await svc.CreateAsync(Admin, Req(other), "https://rmm.example.com")).Value!;
        Assert.Equal([mine.Id], (await svc.ListAsync(scoped)).Value!.Select(x => x.Id));
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.DeleteAsync(scoped, theirs.Id)).Status);
        Assert.True((await svc.DeleteAsync(scoped, mine.Id)).IsOk);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.ScriptAsync(mine.Link.Split('/').Last())).Status);   // a deleted link stops working
    }

    [PostgresFact]
    public async Task BadRequestsAreRejected()
    {
        var (svc, db, _, _, site) = await SetupAsync(); await using var _ = db;
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreateAsync(Admin, new DeploymentRequest { ClientId = site.ClientId, SiteId = site.Id, AgentType = "laptop" }, "https://rmm.example.com")).Status);
        Assert.Equal(OutcomeStatus.NotFound, (await svc.CreateAsync(Admin, new DeploymentRequest { ClientId = 999, SiteId = 999, ExpiresHours = 24 }, "https://rmm.example.com")).Status);
    }
}
