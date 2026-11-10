using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

/// <summary>Server -> agent channel over NATS (subject = agent_id, payload msgpack).</summary>
public interface IAgentCommandBus
{
    /// <summary>Request/reply. Returns "timeout" / "natsdown" on failure (same behaviour as Django).</summary>
    Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default);
    /// <summary>Fire-and-forget.</summary>
    Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default);
}

public interface ITokenService
{
    /// <summary>Token dashboard (SHA-512 hash, expiry + auto-refresh). The raw token is returned only once.</summary>
    Task<(string RawToken, DateTime Expiry)> CreateUserTokenAsync(int userId, CancellationToken ct = default);
    /// <summary>Token agent (plaintext, no expiry) — also the agent's NATS password.</summary>
    Task<string> CreateAgentTokenAsync(int userId, CancellationToken ct = default);
    /// <summary>Token for the installer account with a fixed lifetime (never auto-refreshed). The raw token is returned only once.</summary>
    Task<(string RawToken, DateTime Expiry)> CreateInstallerTokenAsync(int installerUserId, TimeSpan lifetime, CancellationToken ct = default);
    /// <summary>Checks the agent token, then the dashboard token. Returns the full user with Role + scope.</summary>
    Task<User?> ValidateAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, CancellationToken ct = default);
}

public interface INatsConfigWriter
{
    /// <summary>Rewrites nats-rmm.conf (agent user list) and reloads nats-server.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest req, string? ip, CancellationToken ct = default);
    Task LogoutAsync(string rawToken, CancellationToken ct = default);
}

public interface IPermissionService
{
    bool HasPermission(User user, string permission);
    Task<bool> HasPermissionOnAgentAsync(User user, string agentId, CancellationToken ct = default);
}

public interface IAuditService
{
    Task LogAsync(string username, string action, string objectType, string message,
                  string? agentHostname = null, int? agentId = null, string? debugInfo = null, CancellationToken ct = default);
}

public interface IAgentService
{
    Task<IReadOnlyList<AgentListItem>> ListAsync(User user, CancellationToken ct = default);
    Task<Agent?> GetByAgentIdAsync(string agentId, CancellationToken ct = default);
    Task<Agent?> GetByUserAsync(int userId, CancellationToken ct = default);
    Task<NewAgentResult> RegisterAsync(NewAgentRequest req, string installerUser, string? ip, CancellationToken ct = default);
    Task DeleteAsync(string agentId, string username, CancellationToken ct = default);
    Task<object?> RunScriptAsync(string agentId, RunScriptRequest req, string username, CancellationToken ct = default);
}

public interface ICheckService
{
    Task<IReadOnlyList<Check>> GetChecksForAgentAsync(Agent agent, CancellationToken ct = default);
    Task<CheckRunnerResponse> GetRunnerAsync(Agent agent, bool dueOnly, CancellationToken ct = default);
    Task<string> HandleResultAsync(Agent agent, CheckResultPayload payload, CancellationToken ct = default);
}

public interface ITaskService
{
    Task<TaskRunnerResponse?> GetForAgentAsync(Agent agent, int taskId, CancellationToken ct = default);
    Task SaveResultAsync(Agent agent, int taskId, TaskResultPayload payload, CancellationToken ct = default);
    Task RunNowAsync(Agent agent, int taskId, CancellationToken ct = default);
}

public interface IAlertService
{
    Task RaiseCheckFailureAsync(Agent agent, Check check, CheckResult result, CancellationToken ct = default);
    Task ResolveCheckAsync(Agent agent, Check check, CancellationToken ct = default);
    Task RaiseTaskFailureAsync(Agent agent, AutomatedTask task, TaskResult result, CancellationToken ct = default);
    Task ResolveTaskAsync(Agent agent, AutomatedTask task, CancellationToken ct = default);
    Task RaiseAvailabilityAsync(Agent agent, CancellationToken ct = default);
    Task ResolveAvailabilityAsync(Agent agent, CancellationToken ct = default);
}

public interface IWinUpdateService
{
    /// <summary>PUT winupdates: save needs_reboot, reboot if the policy requires it, clean up superseded updates.</summary>
    Task SetRebootStateAsync(Agent agent, bool needsReboot, CancellationToken ct = default);
    /// <summary>PATCH winupdates: record the install result of one update. false if the guid is not found.</summary>
    Task<bool> MarkResultAsync(Agent agent, string guid, bool success, CancellationToken ct = default);
    /// <summary>POST winupdates: sync the WUA list from the agent. false if the payload is empty.</summary>
    Task<bool> SyncAsync(Agent agent, IReadOnlyList<WinUpdateItem> updates, CancellationToken ct = default);
    Task RemoveByGuidAsync(Agent agent, string guid, CancellationToken ct = default);
}

public interface IAgentTelemetryService
{
    Task SetMeshNodeIdAsync(Agent agent, string nodeId, CancellationToken ct = default);
    Task SetChocoInstalledAsync(Agent agent, bool installed, CancellationToken ct = default);
    Task SaveSoftwareAsync(Agent agent, string softwareJson, CancellationToken ct = default);
    /// <summary>PATCH histresult. false if the history entry does not belong to this agent.</summary>
    Task<bool> SaveHistoryResultAsync(Agent agent, long historyId, string scriptResultsJson, string? stdout, int userId, CancellationToken ct = default);
}

public class MeshUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IMeshService
{
    /// <summary>Downloads the MeshAgent installer from the Mesh server for the device group. Throws MeshUnavailableException.</summary>
    Task<byte[]> DownloadAgentAsync(int ident, string plat, CancellationToken ct = default);
}
