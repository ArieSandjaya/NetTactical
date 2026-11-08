using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Infrastructure.Services;

internal static class ScopeGuard
{
    /// <summary>The Agent must be loaded with its Site.</summary>
    public static bool CanSeeAgent(User u, Agent a) => PermissionEvaluator.CanSeeSite(u, a.Site);
    public static bool CanSeeClient(User u, int clientId) => PermissionEvaluator.CanSeeSite(u, new Site { Id = -1, ClientId = clientId });
}
