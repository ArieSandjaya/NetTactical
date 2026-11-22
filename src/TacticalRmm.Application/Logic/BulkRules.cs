using System.Collections;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;

namespace TacticalRmm.Application.Logic;

/// <summary>Validation and result reading for actions that run on several assets at once. Pure logic.</summary>
public static class BulkRules
{
    public const int MaxTargets = 100, MaxOutput = 1000, MaxCommand = 4000, MaxTimeout = 300;
    public const string Command = "command", Script = "script", Reboot = "reboot", MaintenanceOn = "maintenance_on", MaintenanceOff = "maintenance_off", AgentUpdateAction = "agent_update", InstallMsiAction = "install_msi", HardwareRefreshAction = "hardware_refresh";

    public const string Queued = "queued", Running = "running", Ok = "ok", Failed = "failed", Offline = "offline";

    public static readonly IReadOnlyList<string> Actions = [Command, Script, Reboot, MaintenanceOn, MaintenanceOff, AgentUpdateAction, InstallMsiAction, HardwareRefreshAction];
    private static readonly string[] Shells = ["cmd", "powershell", "/bin/bash"];

    /// <summary>The permission needed to run the action, or null for an unknown action.</summary>
    public static string? RequiredPermission(string? action) => action switch
    {
        Command => Perm.RunCommands,
        Script => Perm.RunScripts,
        Reboot => Perm.RebootAgents,
        MaintenanceOn or MaintenanceOff => Perm.EditAgent,
        AgentUpdateAction => Perm.UpdateAgents,
        InstallMsiAction => Perm.ManageSoftware,
        HardwareRefreshAction => Perm.ViewHardware,
        _ => null,
    };

    /// <summary>An error text, or null when the request is acceptable.</summary>
    public static string? Validate(BulkRequest r)
    {
        if (RequiredPermission(r.Action) is null) return $"action must be one of: {string.Join(", ", Actions)}";
        var ids = r.AgentIds ?? [];
        if (ids.Count == 0) return "Select at least one asset.";
        if (ids.Count > MaxTargets) return $"At most {MaxTargets} assets at once.";
        if (ids.Any(i => !AgentIdValidator.IsValid(i))) return "An asset id is not valid.";
        if (ids.Distinct().Count() != ids.Count) return "An asset is listed twice.";
        if (r.Action is Command or Script && (r.Timeout < 1 || r.Timeout > MaxTimeout)) return $"timeout must be between 1 and {MaxTimeout} seconds.";
        if (r.Action == Command)
        {
            if (string.IsNullOrWhiteSpace(r.Cmd)) return "Enter a command.";
            if (r.Cmd.Length > MaxCommand) return $"The command is longer than {MaxCommand} characters.";
            if (!Shells.Contains(r.Shell)) return "shell must be cmd, powershell or /bin/bash.";
        }
        if (r.Action == Script && r.Script <= 0) return "Choose a script.";
        if (r.Action == InstallMsiAction && MsiInstall.Validate(r.Url, r.Sha256, r.InstallArgs, r.Timeout) is { } bad) return bad;
        return null;
    }

    public static string Truncate(string? s)
    {
        var t = (s ?? "").Trim();
        return t.Length > MaxOutput ? t[..MaxOutput] + "…" : t;
    }

    /// <summary>
    /// Turns what an agent answered into a target status and message. "timeout" and "natsdown" mean the agent could not be reached; a script
    /// result map (stdout, stderr, retcode) fails when retcode is not 0; for a reboot only the word "ok" counts.
    /// </summary>
    public static (string Status, string? Message) FromReply(string action, object? reply)
    {
        if (action is MaintenanceOn or MaintenanceOff)   // changed in the database; the agent is not asked
            return reply as string == "ok" ? (Ok, action == MaintenanceOn ? "Maintenance mode is on." : "Maintenance mode is off.") : (Failed, "Could not change it.");
        if (action is AgentUpdateAction or InstallMsiAction or HardwareRefreshAction)   // reply is "started:/skipped:/ok:<text>", or "fail:<text>" / "offline:<text>" from the update or install code
        {
            var t = Convert.ToString(reply) ?? "";
            var i = t.IndexOf(':');
            var (k, m) = i < 0 ? (t, "") : (t[..i], t[(i + 1)..]);
            return k switch { "started" or "skipped" or "ok" => (Ok, m), "offline" => (Offline, m), _ => (Failed, m) };
        }
        if (reply is string s)
        {
            if (s == "timeout") return (Offline, "The agent did not answer (it may be offline).");
            if (s == "natsdown") return (Failed, "The NATS server is not reachable.");
            if (s == "malformed") return (Failed, "The agent sent a reply that could not be read.");
            if (action == Reboot) return s == "ok" ? (Ok, "Restart started.") : (Failed, Truncate(s));
            return (Ok, Truncate(s));
        }
        if (reply is IDictionary map)
        {
            object? Get(string k) { foreach (DictionaryEntry e in map) if (string.Equals(e.Key?.ToString(), k, StringComparison.OrdinalIgnoreCase)) return e.Value; return null; }
            var text = string.Join("\n", new[] { Get("stdout"), Get("stderr") }.Select(Convert.ToString).Where(x => !string.IsNullOrWhiteSpace(x)));
            var ok = Get("retcode") is null || Convert.ToString(Get("retcode"), System.Globalization.CultureInfo.InvariantCulture) == "0";
            return (ok ? Ok : Failed, Truncate(text));
        }
        return action == Reboot ? (Failed, "Unexpected reply from the agent.") : (Ok, Truncate(Convert.ToString(reply, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
