using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;
using TacticalRmm.Infrastructure.Remote;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>
/// VNC passwords with fallback: the lowest-priority enabled password is the default, the next ones are tried only if the server rejects it.
/// The password that worked last time for an asset is tried first, which keeps failed attempts (and server-side lockouts) to a minimum.
/// </summary>
public sealed class VncService(AppDbContext db, IAuditService audit, IOptions<TacticalOptions> opt) : IVncService
{
    // assetId -> credentialId that worked last. In memory only: after a restart the default order applies again.
    private static readonly ConcurrentDictionary<int, int> LastGood = new();

    private string Secret => opt.Value.SecretKey;

    // ---------------------------------------------------------------------------------------------- credentials CRUD
    public async Task<Outcome<IReadOnlyList<VncCredentialDto>>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.VncCredentials.AsNoTracking().OrderBy(c => c.Priority).ThenBy(c => c.Id).ToListAsync(ct);
        var firstEnabled = rows.FirstOrDefault(r => r.Enabled)?.Id;
        return Outcome.Ok<IReadOnlyList<VncCredentialDto>>(rows.Select(r =>
            new VncCredentialDto(r.Id, r.Label, r.Priority, r.Enabled, r.Id == firstEnabled, VncSecrets.Unprotect(r.Secret, Secret) is not null, r.CreatedOn)).ToList());
    }

    public async Task<Outcome<VncCredentialDto>> CreateAsync(User caller, VncCredentialUpsert req, CancellationToken ct = default)
    {
        var label = req.Label?.Trim();
        if (string.IsNullOrEmpty(label) || label.Length > 100) return Outcome.Invalid<VncCredentialDto>("label is required (max 100 characters)");
        if (VncAuth.ValidatePassword(req.Password) is { } bad) return Outcome.Invalid<VncCredentialDto>(bad);
        if (await db.VncCredentials.AnyAsync(c => c.Label == label, ct)) return Outcome.Conflict<VncCredentialDto>("A VNC password with this label already exists");

        var next = req.Priority ?? ((await db.VncCredentials.MaxAsync(c => (int?)c.Priority, ct) ?? 0) + 1);
        var row = new VncCredential { Label = label, Priority = next, Enabled = req.Enabled ?? true, Secret = VncSecrets.Protect(req.Password!, Secret), CreatedBy = caller.Username };
        db.VncCredentials.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "vnc_credential", $"{caller.Username} added VNC password '{label}'", ct: ct);
        return Outcome.Ok(new VncCredentialDto(row.Id, row.Label, row.Priority, row.Enabled, false, true, row.CreatedOn));
    }

    public async Task<Outcome<VncCredentialDto>> UpdateAsync(User caller, int id, VncCredentialUpsert req, CancellationToken ct = default)
    {
        var row = await db.VncCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return Outcome.NotFound<VncCredentialDto>();
        if (req.Label is not null)
        {
            var label = req.Label.Trim();
            if (label.Length is 0 or > 100) return Outcome.Invalid<VncCredentialDto>("label must be 1-100 characters");
            if (await db.VncCredentials.AnyAsync(c => c.Label == label && c.Id != id, ct)) return Outcome.Conflict<VncCredentialDto>("A VNC password with this label already exists");
            row.Label = label;
        }
        var changedPassword = false;
        if (!string.IsNullOrEmpty(req.Password))
        {
            if (VncAuth.ValidatePassword(req.Password) is { } bad) return Outcome.Invalid<VncCredentialDto>(bad);
            row.Secret = VncSecrets.Protect(req.Password, Secret); changedPassword = true;
        }
        if (req.Priority is { } p) row.Priority = p;
        if (req.Enabled is { } en) row.Enabled = en;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "vnc_credential",
            $"{caller.Username} modified VNC password '{row.Label}'{(changedPassword ? " (password changed)" : "")}", ct: ct);
        var first = await db.VncCredentials.AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Priority).ThenBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        return Outcome.Ok(new VncCredentialDto(row.Id, row.Label, row.Priority, row.Enabled, row.Id == first, true, row.CreatedOn));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var row = await db.VncCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return Outcome.NotFound<bool>();
        db.VncCredentials.Remove(row);
        await db.SaveChangesAsync(ct);
        foreach (var kv in LastGood.Where(kv => kv.Value == id).ToList()) LastGood.TryRemove(kv.Key, out _);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "vnc_credential", $"{caller.Username} deleted VNC password '{row.Label}'", ct: ct);
        return Outcome.Ok(true);
    }

    // ---------------------------------------------------------------------------------------------- connect
    public async Task<Outcome<VncTestDto>> TestAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.Include(x => x.Site).AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<VncTestDto>("Asset not found");
        if (!ScopeGuard.CanSeeAgent(caller, a)) return Outcome.Forbidden<VncTestDto>();
        var (info, _) = await ConnectAsync(a, ct);
        await audit.LogAsync(caller.Username, AuditAction.View, "agent", $"{caller.Username} tested VNC to {a.Hostname}: {info.Status}", a.Hostname, a.Id, ct: ct);
        return Outcome.Ok(info);
    }

    public async Task<Outcome<VncFileDto>> PrepareFileAsync(User caller, string agentId, CancellationToken ct = default)
    {
        var a = await db.Agents.Include(x => x.Site).AsNoTracking().FirstOrDefaultAsync(x => x.AgentId == agentId, ct);
        if (a is null) return Outcome.NotFound<VncFileDto>("Asset not found");
        if (!ScopeGuard.CanSeeAgent(caller, a)) return Outcome.Forbidden<VncFileDto>();
        var (info, password) = await ConnectAsync(a, ct);
        await audit.LogAsync(caller.Username, AuditAction.View, "agent", $"{caller.Username} opened a VNC connection to {a.Hostname} ({info.Host}): {info.Status}", a.Hostname, a.Id, ct: ct);
        if (info.Status != VncStatus.Ok || password is null || info.Host is null) return Outcome.Conflict<VncFileDto>(info.Message);

        var safe = new string(a.Hostname.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_').ToArray());
        return Outcome.Ok(new VncFileDto($"{(safe.Length == 0 ? "asset" : safe)}.vnc", VncAuth.BuildConnectionFile(info.Host, info.Port, password), info));
    }

    private async Task<(VncTestDto Info, string? Password)> ConnectAsync(Agent a, CancellationToken ct)
    {
        var o = opt.Value.Vnc;
        int attempts = 0;
        VncTestDto Fail(string status, string msg, string? host = null, string? cred = null) => new(status, msg, host, o.Port, cred, attempts);

        // Only hosts from the asset's own record are contacted (never request input), LAN-only unless explicitly allowed.
        var hosts = VncTargets.Candidates(a.WmiDetail, a.PublicIp, o.AllowPublicTargets);
        if (hosts.Count == 0) return (Fail(VncStatus.NoAddress, "No LAN address is known for this asset yet (it is reported by the agent's hardware inventory)."), null);

        var rows = await db.VncCredentials.AsNoTracking().Where(c => c.Enabled).OrderBy(c => c.Priority).ThenBy(c => c.Id).ToListAsync(ct);
        var creds = rows.Select(r => (r.Id, r.Label, Password: VncSecrets.Unprotect(r.Secret, Secret))).Where(c => c.Password is not null).ToList();
        if (creds.Count == 0) return (Fail(VncStatus.NoCredentials, "No usable VNC password is configured (add one in Settings > VNC passwords)."), null);
        if (LastGood.TryGetValue(a.Id, out var goodId) && creds.FindIndex(c => c.Id == goodId) is var gi and > 0)
        { var g = creds[gi]; creds.RemoveAt(gi); creds.Insert(0, g); }

        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(o.TimeoutMs, 500, 15000));
        var max = Math.Clamp(o.MaxAttempts, 1, 10);
        foreach (var host in hosts)
        {
            VncProbeStatus last = VncProbeStatus.Unreachable;
            foreach (var c in creds)
            {
                if (attempts >= max) break;
                attempts++;
                last = await VncProbe.TryAsync(host, o.Port, c.Password!, timeout, ct);
                if (last == VncProbeStatus.Ok)
                {
                    LastGood[a.Id] = c.Id;
                    return (new VncTestDto(VncStatus.Ok, $"Connected to {host}:{o.Port} using '{c.Label}'.", host, o.Port, c.Label, attempts), c.Password);
                }
                if (last != VncProbeStatus.BadPassword) break;   // unreachable / refused / unsupported: another password will not help
            }
            switch (last)
            {
                case VncProbeStatus.Unreachable: continue;       // try the asset's next address
                case VncProbeStatus.BadPassword:
                    return (Fail(VncStatus.BadPassword, $"{host}:{o.Port} rejected {attempts} stored password(s).", host), null);
                case VncProbeStatus.TooManyAttempts:
                    return (Fail(VncStatus.TooManyAttempts, $"{host} is temporarily refusing logins (too many failed attempts). Wait a minute and try again.", host), null);
                case VncProbeStatus.Unsupported:
                    return (Fail(VncStatus.Unsupported, $"{host}:{o.Port} does not offer VNC password authentication.", host), null);
                default:
                    return (Fail(VncStatus.Error, $"{host}:{o.Port} answered, but not like a VNC server.", host), null);
            }
        }
        return (Fail(VncStatus.Unreachable, $"Could not reach VNC on {string.Join(", ", hosts)} (port {o.Port}). Is TightVNC running and allowed through the firewall?"), null);
    }
}
