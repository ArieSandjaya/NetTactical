using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

/// <summary>Jalur server -> agent lewat NATS (subject = agent_id, payload msgpack).</summary>
public interface IAgentCommandBus
{
    /// <summary>Request/reply. Mengembalikan "timeout" / "natsdown" bila gagal (perilaku sama dengan Django).</summary>
    Task<object?> RequestAsync(string agentId, IDictionary<string, object?> data, TimeSpan timeout, CancellationToken ct = default);
    /// <summary>Fire-and-forget.</summary>
    Task PublishAsync(string agentId, IDictionary<string, object?> data, CancellationToken ct = default);
}

public interface ITokenService
{
    /// <summary>Token dashboard (hash SHA-512, kadaluarsa + auto-refresh). Token mentah hanya dikembalikan sekali.</summary>
    Task<(string RawToken, DateTime Expiry)> CreateUserTokenAsync(int userId, CancellationToken ct = default);
    /// <summary>Token agent (plaintext, tanpa kadaluarsa) — juga password NATS agent.</summary>
    Task<string> CreateAgentTokenAsync(int userId, CancellationToken ct = default);
    /// <summary>Memeriksa token agent lalu token dashboard. Mengembalikan user lengkap dengan Role + scope.</summary>
    Task<User?> ValidateAsync(string rawToken, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, CancellationToken ct = default);
}

public interface INatsConfigWriter
{
    /// <summary>Menulis ulang nats-rmm.conf (daftar user agent) dan reload nats-server.</summary>
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
    /// <summary>PUT winupdates: simpan needs_reboot, reboot bila kebijakan mengharuskan, bersihkan update superseded.</summary>
    Task SetRebootStateAsync(Agent agent, bool needsReboot, CancellationToken ct = default);
    /// <summary>PATCH winupdates: tandai hasil instalasi satu update. false bila guid tidak ditemukan.</summary>
    Task<bool> MarkResultAsync(Agent agent, string guid, bool success, CancellationToken ct = default);
    /// <summary>POST winupdates: sinkronisasi daftar WUA dari agent. false bila payload kosong.</summary>
    Task<bool> SyncAsync(Agent agent, IReadOnlyList<WinUpdateItem> updates, CancellationToken ct = default);
    Task RemoveByGuidAsync(Agent agent, string guid, CancellationToken ct = default);
}

public interface IAgentTelemetryService
{
    Task SetMeshNodeIdAsync(Agent agent, string nodeId, CancellationToken ct = default);
    Task SetChocoInstalledAsync(Agent agent, bool installed, CancellationToken ct = default);
    Task SaveSoftwareAsync(Agent agent, string softwareJson, CancellationToken ct = default);
    /// <summary>PATCH histresult. false bila riwayat bukan milik agent ini.</summary>
    Task<bool> SaveHistoryResultAsync(Agent agent, long historyId, string scriptResultsJson, string? stdout, int userId, CancellationToken ct = default);
}

public class MeshUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IMeshService
{
    /// <summary>Mengunduh installer MeshAgent dari server Mesh sesuai grup perangkat. Melempar MeshUnavailableException.</summary>
    Task<byte[]> DownloadAgentAsync(int ident, string plat, CancellationToken ct = default);
}
