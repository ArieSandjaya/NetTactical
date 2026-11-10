using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class UserAdminService(AppDbContext db, IPasswordHasher hasher, IAuditService audit) : IUserAdminService
{
    private IQueryable<User> Humans => db.Users.Include(u => u.Role).Where(u => !u.IsInstallerUser && u.AgentId == null);

    private static UserDto ToDto(User u) => new(u.Id, u.Username, u.Email, u.IsActive, u.IsSuperuser, u.BlockDashboardLogin,
        u.RoleId, u.Role?.Name, !string.IsNullOrEmpty(u.TotpKey), u.LastLoginIp);   // PasswordHash & TotpKey never leave the service

    public async Task<Outcome<IReadOnlyList<UserDto>>> ListAsync(User caller, CancellationToken ct = default)
    {
        var list = await Humans.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
        IReadOnlyList<UserDto> dtos = list.Select(ToDto).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<UserDto>> GetAsync(User caller, int id, CancellationToken ct = default)
    {
        var u = await Humans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? Outcome.NotFound<UserDto>() : Outcome.Ok(ToDto(u));
    }

    public async Task<Outcome<UserDto>> CreateAsync(User caller, UserCreate req, CancellationToken ct = default)
    {
        var username = (req.Username ?? "").Trim();
        var err = AccountRules.ValidateUsername(username) ?? AccountRules.ValidatePassword(req.Password, username);
        if (err is not null) return Outcome.Invalid<UserDto>(err);
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct)) return Outcome.Conflict<UserDto>("Username already exists");

        Role? role = null;
        if (req.RoleId is { } rid)
        {
            role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct);
            if (role is null) return Outcome.Invalid<UserDto>("role not found");
        }
        if (!AccountRules.CanGrant(caller, req.IsSuperuser || (role?.IsSuperuser ?? false), role?.Permissions ?? []))
            return Outcome.Forbidden<UserDto>("You cannot grant more privileges than you have");

        var user = new User
        {
            Username = username, Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            PasswordHash = hasher.Hash(req.Password), IsSuperuser = req.IsSuperuser, BlockDashboardLogin = req.BlockDashboardLogin,
            RoleId = role?.Id, IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "user", $"{caller.Username} created user {username}", ct: ct);
        user.Role = role;
        return Outcome.Ok(ToDto(user));
    }

    public async Task<Outcome<UserDto>> UpdateAsync(User caller, int id, UserUpdate req, CancellationToken ct = default)
    {
        var user = await Humans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Outcome.NotFound<UserDto>();
        if (!AccountRules.CanManageUser(caller, user)) return Outcome.Forbidden<UserDto>("Only a superuser can modify a superuser");

        var self = caller.Id == user.Id;
        var losingPower = (req.IsActive == false) || (req.IsSuperuser == false && user.IsSuperuser) || req.ClearRole == true || req.RoleId is not null;
        if (self && (req.IsActive == false)) return Outcome.Invalid<UserDto>("You cannot deactivate your own account");

        var wasSuper = PermissionEvaluator.IsSuper(user);
        if (req.Email is not null) user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        if (req.BlockDashboardLogin is { } b) user.BlockDashboardLogin = b;
        if (req.IsActive is { } act) user.IsActive = act;
        if (req.IsSuperuser is { } su) user.IsSuperuser = su;

        if (req.ClearRole == true) { user.RoleId = null; user.Role = null; }
        else if (req.RoleId is { } rid)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == rid, ct);
            if (role is null) return Outcome.Invalid<UserDto>("role not found");
            user.RoleId = role.Id; user.Role = role;
        }

        if (!AccountRules.CanGrant(caller, user.IsSuperuser, user.Role?.Permissions ?? []) && (req.IsSuperuser == true || req.RoleId is not null))
            return Outcome.Forbidden<UserDto>("You cannot grant more privileges than you have");

        // Never let the system lose its last active superuser.
        if (wasSuper && (!PermissionEvaluator.IsSuper(user) || !user.IsActive) && !await AnotherActiveSuperAsync(user.Id, ct))
            return Outcome.Conflict<UserDto>("At least one active superuser must remain");
        _ = losingPower;

        await db.SaveChangesAsync(ct);
        if (!user.IsActive || req.BlockDashboardLogin == true) await RevokeTokensAsync(user.Id, ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "user", $"{caller.Username} modified user {user.Username}", ct: ct);
        return Outcome.Ok(ToDto(user));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var user = await Humans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Outcome.NotFound<bool>();
        if (caller.Id == user.Id) return Outcome.Invalid<bool>("You cannot delete your own account");
        if (!AccountRules.CanManageUser(caller, user)) return Outcome.Forbidden<bool>("Only a superuser can delete a superuser");
        if (PermissionEvaluator.IsSuper(user) && !await AnotherActiveSuperAsync(user.Id, ct)) return Outcome.Conflict<bool>("At least one active superuser must remain");

        await RevokeTokensAsync(user.Id, ct);
        db.ApiKeys.RemoveRange(db.ApiKeys.Where(k => k.UserId == user.Id));
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "user", $"{caller.Username} deleted user {user.Username}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> ResetPasswordAsync(User caller, int id, string newPassword, CancellationToken ct = default)
    {
        var user = await Humans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Outcome.NotFound<bool>();
        if (!AccountRules.CanManageUser(caller, user)) return Outcome.Forbidden<bool>("Only a superuser can reset a superuser's password");
        var err = AccountRules.ValidatePassword(newPassword, user.Username);
        if (err is not null) return Outcome.Invalid<bool>(err);

        user.PasswordHash = hasher.Hash(newPassword);
        await db.SaveChangesAsync(ct);
        await RevokeTokensAsync(user.Id, ct);       // all existing sessions are revoked
        await audit.LogAsync(caller.Username, AuditAction.Modify, "user", $"{caller.Username} reset password of {user.Username}", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> ResetTotpAsync(User caller, int id, CancellationToken ct = default)
    {
        var user = await Humans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Outcome.NotFound<bool>();
        if (!AccountRules.CanManageUser(caller, user)) return Outcome.Forbidden<bool>("Only a superuser can reset a superuser's 2FA");
        user.TotpKey = null;
        await db.SaveChangesAsync(ct);
        await RevokeTokensAsync(user.Id, ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "user", $"{caller.Username} reset 2FA of {user.Username}", ct: ct);
        return Outcome.Ok(true);
    }

    public Task<Outcome<TotpSetup>> BeginTotpAsync(User caller, CancellationToken ct = default)
    {
        var secret = Totp.GenerateSecret();
        return Task.FromResult(Outcome.Ok(new TotpSetup(secret, Totp.ProvisioningUri("Tactical RMM", caller.Username, secret))));
    }

    public async Task<Outcome<bool>> EnableTotpAsync(User caller, string secret, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16 || !Totp.Verify(secret, code ?? ""))
            return Outcome.Invalid<bool>("Invalid verification code");
        var user = await db.Users.FirstAsync(u => u.Id == caller.Id, ct);
        user.TotpKey = secret;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "user", $"{caller.Username} enabled 2FA", ct: ct);
        return Outcome.Ok(true);
    }

    public async Task<Outcome<bool>> DisableTotpAsync(User caller, string password, string code, CancellationToken ct = default)
    {
        var user = await db.Users.FirstAsync(u => u.Id == caller.Id, ct);
        if (string.IsNullOrEmpty(user.TotpKey)) return Outcome.Invalid<bool>("2FA is not enabled");
        if (!hasher.Verify(password ?? "", user.PasswordHash) || !Totp.Verify(user.TotpKey, code ?? "")) return Outcome.Invalid<bool>("Wrong password or code");
        user.TotpKey = null;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "user", $"{caller.Username} disabled 2FA", ct: ct);
        return Outcome.Ok(true);
    }

    private Task<bool> AnotherActiveSuperAsync(int exceptId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id != exceptId && u.IsActive && !u.IsInstallerUser && u.AgentId == null
                               && (u.IsSuperuser || (u.Role != null && u.Role.IsSuperuser)), ct);

    private async Task RevokeTokensAsync(int userId, CancellationToken ct)
    {
        db.UserTokens.RemoveRange(db.UserTokens.Where(t => t.UserId == userId));
        await db.SaveChangesAsync(ct);
    }
}
