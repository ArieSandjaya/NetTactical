using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

public static class AgentConfigFactory
{
    public static AgentCheckInConfig Create(AgentRuntimeOptions o, Random? rng = null)
    {
        rng ??= Random.Shared;
        int R(IntRange r) => rng.Next(r.Min, r.Max + 1);
        return new AgentCheckInConfig
        {
            CheckinHello = R(o.Hello), CheckinAgentInfo = R(o.AgentInfo), CheckinWinSvc = R(o.WinSvc),
            CheckinPubIp = R(o.PubIp), CheckinDisks = R(o.Disks), CheckinSw = R(o.Sw),
            CheckinWmi = R(o.Wmi), CheckinSyncMesh = R(o.SyncMesh), LimitData = o.LimitData,
            InstallNushell = o.InstallNushell, InstallNushellVersion = o.NushellVersion,
            InstallNushellUrl = o.NushellUrl, NushellEnableConfig = o.NushellEnableConfig,
            InstallDeno = o.InstallDeno, InstallDenoVersion = o.DenoVersion,
            InstallDenoUrl = o.DenoUrl, DenoDefaultPermissions = o.DenoDefaultPermissions,
        };
    }
}
