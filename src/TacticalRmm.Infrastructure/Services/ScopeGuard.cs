using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Infrastructure.Services;

internal static class ScopeGuard
{
    /// <summary>Agent harus dimuat dengan Site.</summary>
    public static bool CanSeeAgent(User u, Agent a) => PermissionEvaluator.CanSeeSite(u, a.Site);
    public static bool CanSeeClient(User u, int clientId) => PermissionEvaluator.CanSeeSite(u, new Site { Id = -1, ClientId = clientId });
}
