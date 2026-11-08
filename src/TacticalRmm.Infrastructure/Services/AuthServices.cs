using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class AuthService(AppDbContext db, IPasswordHasher hasher, ITokenService tokens, IAuditService audit) : IAuthService
{
    public async Task<LoginResult> LoginAsync(LoginRequest req, string? ip, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username && u.AgentId == null && !u.IsInstallerUser, ct);

        // Verification always runs so that response time does not leak whether a username exists.
        var ok = user is not null && user.IsActive && !user.BlockDashboardLogin
                 && hasher.Verify(req.Password, user.PasswordHash);

        if (ok && !string.IsNullOrEmpty(user!.TotpKey))
        {
            if (string.IsNullOrEmpty(req.TwoFactor)) return new LoginResult(null, null, true, null);
            ok = Totp.Verify(user.TotpKey, req.TwoFactor);
        }

        if (!ok)
        {
            await audit.LogAsync(req.Username, AuditAction.FailedLogin, "user", $"{req.Username} failed to login", debugInfo: ip, ct: ct);
            return new LoginResult(null, null, false, "Bad credentials");
        }

        user!.LastLoginIp = ip;
        await db.SaveChangesAsync(ct);
        var (raw, expiry) = await tokens.CreateUserTokenAsync(user.Id, ct);
        await audit.LogAsync(user.Username, AuditAction.Login, "user", $"{user.Username} logged in successfully", debugInfo: ip, ct: ct);
        return new LoginResult(raw, expiry, false, null);
    }

    public Task LogoutAsync(string rawToken, CancellationToken ct = default) => tokens.RevokeAsync(rawToken, ct);
}

public sealed class PermissionService(AppDbContext db) : IPermissionService
{
    public bool HasPermission(User user, string permission) => PermissionEvaluator.Has(user, permission);

    public async Task<bool> HasPermissionOnAgentAsync(User user, string agentId, CancellationToken ct = default)
    {
        var site = await db.Agents.AsNoTracking().Where(a => a.AgentId == agentId).Select(a => a.Site).FirstOrDefaultAsync(ct);
        return site is not null && PermissionEvaluator.CanSeeSite(user, site);
    }
}

public sealed class AuditService(AppDbContext db) : IAuditService
{
    public async Task LogAsync(string username, string action, string objectType, string message,
        string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Username = username, Action = action, ObjectType = objectType, Message = message,
            Agent = agentHostname, AgentId = agentId, EntryTime = DateTime.UtcNow,
            DebugInfo = debugInfo is null ? null : System.Text.Json.JsonSerializer.Serialize(new { ip = debugInfo })
        });
        await db.SaveChangesAsync(ct);
    }
}
