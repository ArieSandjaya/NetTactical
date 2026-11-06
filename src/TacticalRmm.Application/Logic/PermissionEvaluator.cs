using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

public static class PermissionEvaluator
{
    public static bool IsSuper(User u) => u.IsSuperuser || (u.Role?.IsSuperuser ?? false);

    public static bool Has(User u, string perm)
    {
        if (u.IsInstallerUser || u.AgentId != null) return false; // akun agent tidak pernah boleh memakai API dashboard
        if (IsSuper(u)) return true;
        return u.Role is not null && u.Role.Permissions.Contains(perm); // role kosong => ditolak
    }

    /// <summary>Scope per client/site. Role tanpa pembatasan = akses penuh (perilaku sama dengan Django).</summary>
    public static bool CanSeeSite(User u, Site site)
    {
        if (u.IsInstallerUser || u.AgentId != null) return false;
        if (IsSuper(u)) return true;
        var r = u.Role;
        if (r is null) return false;
        if (r.CanViewClients.Count == 0 && r.CanViewSites.Count == 0) return true;
        return r.CanViewSites.Any(s => s.Id == site.Id) || r.CanViewClients.Any(c => c.Id == site.ClientId);
    }
}
