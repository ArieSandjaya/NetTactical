using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Services;
using Xunit;

namespace TacticalRmm.Tests;

public class AppDeploymentPostgresTests
{
    private sealed class Perms(params string[] deny) : IPermissionService
    {
        public bool HasPermission(User user, string permission) => true;
        public Task<bool> HasPermissionOnAgentAsync(User user, string agentId, CancellationToken ct = default) => Task.FromResult(!deny.Contains(agentId));
    }
    private sealed class Audit : IAuditService
    {
        public readonly List<string> Messages = [];
        public Task LogAsync(string username, string action, string objectType, string message, string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default) { Messages.Add(message); return Task.CompletedTask; }
    }
    private sealed class Log : IAgentLogService
    {
        public readonly List<string> Commands = [];
        public Task<Outcome<IReadOnlyList<HistoryEntryDto>>> HistoryAsync(string agentId, int? take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<IReadOnlyList<NoteDto>>> NotesAsync(string agentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<NoteDto>> AddNoteAsync(User caller, string agentId, string? text, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Outcome<bool>> DeleteNoteAsync(User caller, string agentId, int noteId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordCommandAsync(string agentId, string username, string command, CancellationToken ct = default) { Commands.Add(command); return Task.CompletedTask; }
    }

    private const string On = "app-agent-number-0001", Off = "app-agent-number-0002", Lin = "app-agent-number-0003";
    private static readonly User Admin = new() { Id = 1, Username = "admin", IsSuperuser = true };

    private static async Task<(AppDeploymentService svc, AppDbContext db, Audit audit, Log log, Site site, int pkg)> SetupAsync(params string[] deny)
    {
        var db = await PgDb.CreateAsync();
        var c = new Client { Name = "Acme" }; var s = new Site { Name = "Main", Client = c };
        db.AddRange(c, s,
            new Agent { AgentId = On, Hostname = "PC-ON", Site = s, LastSeen = DateTime.UtcNow },
            new Agent { AgentId = Off, Hostname = "PC-OFF", Site = s, LastSeen = DateTime.UtcNow.AddHours(-3) },
            new Agent { AgentId = Lin, Hostname = "LINUX", Site = s, Plat = "linux", LastSeen = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var audit = new Audit(); var log = new Log();
        var svc = new AppDeploymentService(db, new Perms(deny), audit, log);
        var pkg = (await svc.CreatePackageAsync(Admin, new SoftwarePackageRequest { Name = "Acrobat", InstallerType = "exe", Source = "https://files.example.com/a.exe", SilentArgs = "/sAll", DetectName = "Adobe Acrobat" })).Value!;
        return (svc, db, audit, log, s, pkg.Id);
    }

    [PostgresFact]
    public async Task APackageCanBeAddedChangedAndDeletedAndNamesStayUnique()
    {
        var (svc, db, _, _, _, id) = await SetupAsync(); await using var _ = db;
        var dup = await svc.CreatePackageAsync(Admin, new SoftwarePackageRequest { Name = "Acrobat", InstallerType = "msi", Source = "https://files.example.com/b.msi" });
        Assert.Equal(OutcomeStatus.Conflict, dup.Status);
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreatePackageAsync(Admin, new SoftwarePackageRequest { Name = "Bad", InstallerType = "exe", Source = "https://files.example.com/b.exe" })).Status);
        var changed = await svc.UpdatePackageAsync(Admin, id, new SoftwarePackageRequest { Name = "Acrobat 2", InstallerType = "exe", Source = "https://files.example.com/a2.exe", SilentArgs = "/S", Sha256 = new string('a', 64) });
        Assert.Equal(new string('A', 64), changed.Value!.Sha256);
        Assert.True((await svc.DeletePackageAsync(Admin, id)).IsOk);
        Assert.Empty((await svc.ListPackagesAsync()).Value!);
    }

    [PostgresFact]
    public async Task ADeploymentTargetsOnlyWindowsAssetsOfTheChosenSite()
    {
        var (svc, db, audit, _, site, pkg) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, SiteIds = [site.Id] })).Value!;
        Assert.Equal(2, d.Total);              // the Linux asset is left out
        Assert.Equal(2, d.Pending);
        Assert.Equal(["PC-OFF", "PC-ON"], d.Targets!.Select(t => t.Hostname).ToArray());
        Assert.Contains(audit.Messages, m => m.Contains("started a deployment of Acrobat"));
        Assert.Equal(OutcomeStatus.Invalid, (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, AgentIds = [Lin] })).Status);   // nothing Windows matched
        Assert.Equal(OutcomeStatus.NotFound, (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = 999, SiteIds = [site.Id] })).Status);
    }

    [PostgresFact]
    public async Task OnlyOnlineAssetsAreClaimedOnceAndTheOfflineOneWaits()
    {
        var (svc, db, _, log, site, pkg) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, SiteIds = [site.Id], Reinstall = false })).Value!;
        var work = await svc.ClaimDueAsync(8);
        var w = Assert.Single(work);
        Assert.Equal(On, w.AgentId);
        Assert.Contains("Adobe Acrobat", w.Script);
        Assert.Contains(log.Commands, c => c.Contains("[deploy] Acrobat"));
        Assert.Empty(await svc.ClaimDueAsync(8));          // already running: no second claim

        await svc.FinishAsync(w, "Installed (exit code 0).", null);
        var after = (await svc.GetAsync(Admin, d.Id)).Value!;
        Assert.Equal(1, after.Ok); Assert.Equal(1, after.Pending);
        Assert.Equal(AppInstall.Pending, after.Targets!.Single(t => t.Hostname == "PC-OFF").Status);

        // the offline asset comes online: it is picked up now
        await db.Agents.Where(a => a.AgentId == Off).ExecuteUpdateAsync(u => u.SetProperty(a => a.LastSeen, (DateTime?)DateTime.UtcNow));
        var second = Assert.Single(await svc.ClaimDueAsync(8));
        Assert.Equal(Off, second.AgentId);
        await svc.FinishAsync(second, null, "Installer exit code 1603: fatal error during installation");
        var done = (await svc.GetAsync(Admin, d.Id)).Value!;
        Assert.Equal(1, done.Ok); Assert.Equal(1, done.Failed);
    }

    [PostgresFact]
    public async Task AnAnswerOfNoReplyKeepsTheAssetWaitingAndAFailureCanBeRetried()
    {
        var (svc, db, _, _, _, pkg) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, AgentIds = [On] })).Value!;
        var w = Assert.Single(await svc.ClaimDueAsync(8));
        await svc.FinishAsync(w, null, "The computer did not answer in time");
        Assert.Equal(1, (await svc.GetAsync(Admin, d.Id)).Value!.Pending);

        w = Assert.Single(await svc.ClaimDueAsync(8));
        await svc.FinishAsync(w, null, "Installer exit code 1603");
        Assert.Equal(1, (await svc.GetAsync(Admin, d.Id)).Value!.Failed);
        Assert.Empty(await svc.ClaimDueAsync(8));

        db.ChangeTracker.Clear();      // the updates above went straight to the database
        var retried = (await svc.RetryFailedAsync(Admin, d.Id)).Value!;
        Assert.Equal(1, retried.Pending);
        Assert.Single(await svc.ClaimDueAsync(8));
        db.ChangeTracker.Clear();
        Assert.Equal(OutcomeStatus.Conflict, (await svc.RetryFailedAsync(Admin, d.Id)).Status);   // nothing failed now (it is running)
    }

    [PostgresFact]
    public async Task AnExpiredDeploymentGivesUpOnWaitingAssets()
    {
        var (svc, db, _, _, site, pkg) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, SiteIds = [site.Id] })).Value!;
        await db.AppDeployments.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Empty(await svc.ClaimDueAsync(8));
        var after = (await svc.GetAsync(Admin, d.Id)).Value!;
        Assert.Equal(2, after.ExpiredCount); Assert.Equal(0, after.Pending);
        db.ChangeTracker.Clear();
        Assert.True((await svc.RetryFailedAsync(Admin, d.Id)).IsOk);       // retry extends the time and queues them again
    }

    [PostgresFact]
    public async Task AnAssetTheCallerMayNotTouchIsRefusedWhenNamedAndLeftOutWhenReachedThroughASite()
    {
        var (svc, db, _, _, site, pkg) = await SetupAsync(deny: On); await using var _ = db;
        Assert.Equal(OutcomeStatus.Forbidden, (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, AgentIds = [On] })).Status);
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, SiteIds = [site.Id] })).Value!;
        Assert.Equal(["PC-OFF"], d.Targets!.Select(t => t.Hostname).ToArray());
    }

    [PostgresFact]
    public async Task APackageWithWaitingComputersCannotBeDeletedUntilTheDeploymentIsRemoved()
    {
        var (svc, db, _, _, site, pkg) = await SetupAsync(); await using var _ = db;
        var d = (await svc.CreateAsync(Admin, new AppDeploymentRequest { PackageId = pkg, SiteIds = [site.Id] })).Value!;
        Assert.Equal(OutcomeStatus.Conflict, (await svc.DeletePackageAsync(Admin, pkg)).Status);
        Assert.True((await svc.DeleteAsync(Admin, d.Id)).IsOk);
        Assert.True((await svc.DeletePackageAsync(Admin, pkg)).IsOk);
    }
}
