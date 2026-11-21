using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Runs a PowerShell script on an agent through "runscript" and returns what it printed after the @@OK@@ marker, or why that failed.
/// The agent has no commands for files or event logs, so those features send small scripts instead.</summary>
public static class AgentScripts
{
    public static async Task<(string? Payload, string? Error)> RunAsync(IAgentCommandBus bus, string agentId, string script, TimeSpan timeout, CancellationToken ct = default)
    {
        var data = new Dictionary<string, object?>
        {
            ["func"] = "runscript",
            ["timeout"] = (int)timeout.TotalSeconds,
            ["script_args"] = new List<string>(),
            ["payload"] = new Dictionary<string, object?> { ["code"] = script, ["shell"] = "powershell" },
            ["run_as_user"] = false,
            ["env_vars"] = new List<string>(),
            ["nushell_enable_config"] = false,
            ["deno_default_permissions"] = "",
            ["id"] = 0,
        };
        var (text, err) = FileBrowser.Output(await bus.RequestAsync(agentId, data, timeout + TimeSpan.FromSeconds(3), ct));
        return err is not null ? (null, err) : FileBrowser.Payload(text);
    }
}
