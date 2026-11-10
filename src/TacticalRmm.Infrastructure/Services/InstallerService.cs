using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Creates a time-limited installer token and the commands that install the Tactical RMM agent for one client/site.</summary>
public sealed class InstallerService(AppDbContext db, ITokenService tokens, IAuditService audit, IOptions<TacticalOptions> opt) : IInstallerService
{
    public async Task<Outcome<InstallerDto>> GenerateAsync(User caller, InstallerRequest req, string defaultApi, CancellationToken ct = default)
    {
        if (!InstallerCommands.AgentTypes.Contains(req.AgentType)) return Outcome.Invalid<InstallerDto>("agent_type must be workstation or server");
        if (!InstallerCommands.Architectures.Contains(req.Arch)) return Outcome.Invalid<InstallerDto>("arch must be amd64 or 386");
        if (!InstallerCommands.ValidHours.Contains(req.ExpiresHours)) return Outcome.Invalid<InstallerDto>($"expires_hours must be one of {string.Join(", ", InstallerCommands.ValidHours)}");
        var api = string.IsNullOrWhiteSpace(req.Api) ? defaultApi : req.Api;
        if (InstallerCommands.ValidateApi(api) is { } apiError) return Outcome.Invalid<InstallerDto>(apiError);
        api = InstallerCommands.NormalizeApi(api);

        var site = await db.Sites.Include(s => s.Client).AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SiteId && s.ClientId == req.ClientId, ct);
        if (site is null) return Outcome.NotFound<InstallerDto>("Client or site not found");
        if (!PermissionEvaluator.CanSeeSite(caller, site)) return Outcome.Forbidden<InstallerDto>();

        var installer = await db.Users.AsNoTracking().Where(u => u.IsInstallerUser && u.IsActive).OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        if (installer is null) return Outcome.Conflict<InstallerDto>("There is no installer account (it is created on first start).");

        var version = opt.Value.LatestAgentVer;
        var url = InstallerCommands.DownloadUrl(version, req.Arch);
        if (url is null) return Outcome.Conflict<InstallerDto>($"Configured agent version '{version}' is not valid.");
        var file = InstallerCommands.FileName(version, req.Arch);

        var (token, expiry) = await tokens.CreateInstallerTokenAsync(installer.Id, TimeSpan.FromHours(req.ExpiresHours), ct);
        var args = InstallerCommands.InstallArguments(api, site.ClientId, site.Id, req.AgentType, token, req.Rdp, req.Ping, req.Power, noMesh: !req.InstallMesh);

        var warnings = new List<string>();
        if (!api.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) warnings.Add("The API URL is not https. Agents are normally installed against an https URL; put a TLS reverse proxy in front of this server.");
        if (api.Contains("localhost", StringComparison.OrdinalIgnoreCase) || api.Contains("127.0.0.1")) warnings.Add("The API URL points to localhost, which other computers cannot reach. Enter the server's public address.");
        warnings.Add("Anyone with this token can enroll an agent in any client or site until it expires. Share it only with whoever installs the agent.");

        await audit.LogAsync(caller.Username, AuditAction.AgentInstall, "agent",
            $"{caller.Username} generated an agent installer for {site.Client.Name} / {site.Name} ({req.AgentType}, {req.Arch}, valid {req.ExpiresHours}h)", ct: ct);
        return Outcome.Ok(new InstallerDto(site.Client.Name, site.Name, req.AgentType, req.Arch, api, file, url, expiry, token,
            InstallerCommands.CommandPrompt(file, args), InstallerCommands.PowerShell(url, file, args), warnings));
    }
}
