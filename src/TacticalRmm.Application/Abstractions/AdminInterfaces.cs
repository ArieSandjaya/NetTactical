using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Abstractions;

public interface ICheckAdminService
{
    Task<Outcome<IReadOnlyList<CheckDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<CheckDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default);
    Task<Outcome<CheckDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<CheckDto>> CreateAsync(User caller, CheckUpsert req, CancellationToken ct = default);
    Task<Outcome<CheckDto>> UpdateAsync(User caller, int id, CheckUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface ITaskAdminService
{
    Task<Outcome<IReadOnlyList<TaskDto>>> ListForAgentAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<TaskDto>>> ListForPolicyAsync(User caller, int policyId, CancellationToken ct = default);
    Task<Outcome<TaskDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<TaskDto>> CreateAsync(User caller, TaskUpsert req, CancellationToken ct = default);
    Task<Outcome<TaskDto>> UpdateAsync(User caller, int id, TaskUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> RunNowAsync(User caller, int id, string? agentId, CancellationToken ct = default);
}

/// <summary>Pushes task definitions to the agent's Windows Task Scheduler (schedtask / delschedtask).</summary>
public interface ITaskSyncService
{
    Task<int> SyncPendingAsync(CancellationToken ct = default);
    Task SyncOneAsync(int taskId, int agentId, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<Outcome<IReadOnlyList<UserDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<UserDto>> GetAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<UserDto>> CreateAsync(User caller, UserCreate req, CancellationToken ct = default);
    Task<Outcome<UserDto>> UpdateAsync(User caller, int id, UserUpdate req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> ResetPasswordAsync(User caller, int id, string newPassword, CancellationToken ct = default);
    Task<Outcome<bool>> ResetTotpAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Creates a new TOTP secret for the user themselves (not saved until the code is verified).</summary>
    Task<Outcome<TotpSetup>> BeginTotpAsync(User caller, CancellationToken ct = default);
    Task<Outcome<bool>> EnableTotpAsync(User caller, string secret, string code, CancellationToken ct = default);
    /// <summary>Turns off the caller's own 2FA; requires the current password and a valid current code.</summary>
    Task<Outcome<bool>> DisableTotpAsync(User caller, string password, string code, CancellationToken ct = default);
}

public interface IRoleAdminService
{
    IReadOnlyList<string> AllPermissions { get; }
    Task<Outcome<IReadOnlyList<RoleDto>>> ListAsync(User caller, CancellationToken ct = default);
    Task<Outcome<RoleDto>> CreateAsync(User caller, RoleUpsert req, CancellationToken ct = default);
    Task<Outcome<RoleDto>> UpdateAsync(User caller, int id, RoleUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IOrgAdminService
{
    Task<Outcome<bool>> UpdateClientAsync(User caller, int id, ClientUpdate req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteClientAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<SiteDto>> AddSiteAsync(User caller, SiteUpsert req, CancellationToken ct = default);
    Task<Outcome<SiteDto>> UpdateSiteAsync(User caller, int id, SiteUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteSiteAsync(User caller, int id, CancellationToken ct = default);
    Task<Outcome<bool>> UpdateAgentAsync(User caller, string agentId, AgentUpdate req, CancellationToken ct = default);
}

public interface IPolicyAdminService
{
    Task<Outcome<IReadOnlyList<PolicyDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<PolicyDto>> CreateAsync(User caller, PolicyUpsert req, CancellationToken ct = default);
    Task<Outcome<PolicyDto>> UpdateAsync(User caller, int id, PolicyUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IAlertTemplateAdminService
{
    Task<Outcome<IReadOnlyList<AlertTemplateDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<AlertTemplateDto>> CreateAsync(User caller, AlertTemplateUpsert req, CancellationToken ct = default);
    Task<Outcome<AlertTemplateDto>> UpdateAsync(User caller, int id, AlertTemplateUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface ICustomFieldService
{
    Task<Outcome<IReadOnlyList<CustomFieldDto>>> ListAsync(string? model, CancellationToken ct = default);
    Task<Outcome<CustomFieldDto>> CreateAsync(User caller, CustomFieldUpsert req, CancellationToken ct = default);
    Task<Outcome<CustomFieldDto>> UpdateAsync(User caller, int id, CustomFieldUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Every field of the model with this target's value. Target is the client or site id, or the agent_id string.</summary>
    Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> ValuesAsync(User caller, string model, string target, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<CustomFieldValueDto>>> SetValuesAsync(User caller, string model, string target, CustomFieldValuesSet req, CancellationToken ct = default);
}

public interface ISnippetService
{
    Task<Outcome<IReadOnlyList<SnippetDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<SnippetDto>> GetAsync(int id, CancellationToken ct = default);
    Task<Outcome<SnippetDto>> CreateAsync(User caller, SnippetUpsert req, CancellationToken ct = default);
    Task<Outcome<SnippetDto>> UpdateAsync(User caller, int id, SnippetUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
}

public interface IVncService
{
    Task<Outcome<IReadOnlyList<VncCredentialDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<VncCredentialDto>> CreateAsync(User caller, VncCredentialUpsert req, CancellationToken ct = default);
    Task<Outcome<VncCredentialDto>> UpdateAsync(User caller, int id, VncCredentialUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Checks reachability and which stored password works. Always Ok with a status, unless the asset is missing or out of scope.</summary>
    Task<Outcome<VncTestDto>> TestAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>Same check, then builds the connection file. Conflict (with the reason) when it cannot connect.</summary>
    Task<Outcome<VncFileDto>> PrepareFileAsync(User caller, string agentId, CancellationToken ct = default);
}

public interface INotificationAdminService
{
    Task<Outcome<IReadOnlyList<NotificationChannelDto>>> ListAsync(CancellationToken ct = default);
    Task<Outcome<NotificationChannelDto>> CreateAsync(User caller, NotificationChannelUpsert req, CancellationToken ct = default);
    Task<Outcome<NotificationChannelDto>> UpdateAsync(User caller, int id, NotificationChannelUpsert req, CancellationToken ct = default);
    Task<Outcome<bool>> DeleteAsync(User caller, int id, CancellationToken ct = default);
    /// <summary>Sends a sample alert to one channel now and reports the result.</summary>
    Task<Outcome<NotificationTestResult>> TestAsync(User caller, int id, CancellationToken ct = default);
}

/// <summary>Hands alert events to the background sender; never blocks and never throws.</summary>
public interface INotificationQueue { void Enqueue(AlertEvent e); }

public interface IInstallerService
{
    /// <param name="defaultApi">Used when the request does not name the public API URL (the URL of the current request).</param>
    Task<Outcome<InstallerDto>> GenerateAsync(User caller, InstallerRequest req, string defaultApi, CancellationToken ct = default);
}

/// <summary>Runs a PowerShell script on a remote Windows computer over WinRM with explicit credentials.</summary>
public interface IRemoteExecutor
{
    Task<RemoteRunResult> RunAsync(RemoteRunRequest request, CancellationToken ct = default);
}

public interface IPushInstallService
{
    /// <summary>Validates, then starts a background push and returns at once; poll <see cref="GetAsync"/>.</summary>
    Task<Outcome<PushJobDto>> StartAsync(User caller, PushInstallRequest req, string defaultApi, CancellationToken ct = default);
    Task<Outcome<PushJobDto>> GetAsync(User caller, string id, CancellationToken ct = default);
    Task<Outcome<IReadOnlyList<PushJobDto>>> ListAsync(User caller, CancellationToken ct = default);
}

public interface IWinUpdateAdminService
{
    Task<Outcome<IReadOnlyList<WinUpdateDto>>> ListAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<int>> SetActionAsync(User caller, string agentId, WinUpdateActionRequest req, CancellationToken ct = default);
    /// <summary>Asks the agent to scan Windows Update now; the result arrives through the normal agent callback.</summary>
    Task<Outcome<bool>> ScanAsync(User caller, string agentId, CancellationToken ct = default);
    /// <summary>Installs the approved, not yet installed updates now.</summary>
    Task<Outcome<WinUpdateInstallResult>> InstallNowAsync(User caller, string agentId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> GetAgentPolicyAsync(string agentId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> SaveAgentPolicyAsync(User caller, string agentId, WinUpdatePolicyDto req, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> GetPolicyAsync(int policyId, CancellationToken ct = default);
    Task<Outcome<WinUpdatePolicyView>> SavePolicyAsync(User caller, int policyId, WinUpdatePolicyDto req, CancellationToken ct = default);
}
