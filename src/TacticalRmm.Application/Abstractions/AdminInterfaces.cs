using TacticalRmm.Application.Dtos;
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
