using Microsoft.EntityFrameworkCore;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Services;

public sealed class RoleAdminService(AppDbContext db, IAuditService audit) : IRoleAdminService
{
    public IReadOnlyList<string> AllPermissions => Perm.All;

    private static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.IsSuperuser, r.Permissions.ToList(),
        r.CanViewClients.Select(c => c.Id).ToList(), r.CanViewSites.Select(s => s.Id).ToList());

    public async Task<Outcome<IReadOnlyList<RoleDto>>> ListAsync(User caller, CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.CanViewClients).Include(r => r.CanViewSites).OrderBy(r => r.Name).ToListAsync(ct);
        IReadOnlyList<RoleDto> dtos = roles.Select(ToDto).ToList();
        return Outcome.Ok(dtos);
    }

    public async Task<Outcome<RoleDto>> CreateAsync(User caller, RoleUpsert req, CancellationToken ct = default)
    {
        var perms = (req.Permissions ?? []).Distinct().ToList();
        var err = AccountRules.ValidateRole(req.Name, perms);
        if (err is not null) return Outcome.Invalid<RoleDto>(err);
        if (!AccountRules.CanGrant(caller, req.IsSuperuser == true, perms)) return Outcome.Forbidden<RoleDto>("You cannot grant more privileges than you have");
        var name = req.Name!.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name, ct)) return Outcome.Conflict<RoleDto>("A role with this name already exists");

        var role = new Role { Name = name, IsSuperuser = req.IsSuperuser == true, Permissions = perms };
        var scopeErr = await SetScopeAsync(role, req, ct);
        if (scopeErr is not null) return Outcome.Invalid<RoleDto>(scopeErr);

        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Add, "role", $"{caller.Username} created role {name}", ct: ct);
        return Outcome.Ok(ToDto(role));
    }

    public async Task<Outcome<RoleDto>> UpdateAsync(User caller, int id, RoleUpsert req, CancellationToken ct = default)
    {
        var role = await db.Roles.Include(r => r.CanViewClients).Include(r => r.CanViewSites).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Outcome.NotFound<RoleDto>();

        // Mengubah role superuser / role yang melebihi izin sendiri = eskalasi.
        if (!AccountRules.CanGrant(caller, role.IsSuperuser, role.Permissions)) return Outcome.Forbidden<RoleDto>("You cannot modify a role with more privileges than you have");

        var name = req.Name ?? role.Name;
        var perms = req.Permissions is null ? role.Permissions : req.Permissions.Distinct().ToList();
        var isSuper = req.IsSuperuser ?? role.IsSuperuser;
        var err = AccountRules.ValidateRole(name, perms);
        if (err is not null) return Outcome.Invalid<RoleDto>(err);
        if (!AccountRules.CanGrant(caller, isSuper, perms)) return Outcome.Forbidden<RoleDto>("You cannot grant more privileges than you have");
        name = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id, ct)) return Outcome.Conflict<RoleDto>("A role with this name already exists");

        if (role.IsSuperuser && !isSuper && !await AnotherSuperAfterRoleChangeAsync(id, ct))
            return Outcome.Conflict<RoleDto>("At least one active superuser must remain");

        role.Name = name; role.Permissions = perms; role.IsSuperuser = isSuper;
        var scopeErr = await SetScopeAsync(role, req, ct);
        if (scopeErr is not null) return Outcome.Invalid<RoleDto>(scopeErr);

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Modify, "role", $"{caller.Username} modified role {name}", ct: ct);
        return Outcome.Ok(ToDto(role));
    }

    public async Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Outcome.NotFound<bool>();
        if (!AccountRules.CanGrant(caller, role.IsSuperuser, role.Permissions)) return Outcome.Forbidden<bool>("You cannot delete a role with more privileges than you have");
        var users = await db.Users.CountAsync(u => u.RoleId == id, ct);
        if (users > 0) return Outcome.Conflict<bool>($"Role is assigned to {users} user(s); reassign them first");

        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(caller.Username, AuditAction.Delete, "role", $"{caller.Username} deleted role {role.Name}", ct: ct);
        return Outcome.Ok(true);
    }

    private async Task<string?> SetScopeAsync(Role role, RoleUpsert req, CancellationToken ct)
    {
        if (req.CanViewClients is not null)
        {
            var ids = req.CanViewClients.Distinct().ToList();
            var clients = await db.Clients.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
            if (clients.Count != ids.Count) return "One or more clients in can_view_clients do not exist";
            role.CanViewClients = clients;
        }
        if (req.CanViewSites is not null)
        {
            var ids = req.CanViewSites.Distinct().ToList();
            var sites = await db.Sites.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            if (sites.Count != ids.Count) return "One or more sites in can_view_sites do not exist";
            role.CanViewSites = sites;
        }
        return null;
    }

    private Task<bool> AnotherSuperAfterRoleChangeAsync(int roleId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.IsActive && !u.IsInstallerUser && u.AgentId == null
                               && (u.IsSuperuser || (u.Role != null && u.Role.IsSuperuser && u.Role.Id != roleId)), ct);
}
