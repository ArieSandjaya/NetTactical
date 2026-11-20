using System.Collections;
using System.Globalization;
using TacticalRmm.Application.Dtos;

namespace TacticalRmm.Application.Logic;

/// <summary>Windows services and running processes of an asset: input checks and reading the agent's replies. Pure logic.</summary>
public static class RemoteTools
{
    public const int MaxRows = 5000, MaxText = 300;
    public static readonly IReadOnlyList<string> ServiceActions = ["start", "stop", "restart", "disable", "enable"];

    /// <summary>The agent command for a service action: start, stop and restart act on the running service (winsvcaction); disable and enable change
    /// the startup type (editwinsvc: disabled, or manual for enable) and leave a running service running.</summary>
    public static IDictionary<string, object?> ServiceCommand(string name, string action) => action is "disable" or "enable"
        ? new Dictionary<string, object?> { ["func"] = "editwinsvc", ["payload"] = new Dictionary<string, object?> { ["name"] = name, ["startType"] = action == "disable" ? "disabled" : "manual" } }
        : new Dictionary<string, object?> { ["func"] = "winsvcaction", ["payload"] = new Dictionary<string, object?> { ["name"] = name, ["action"] = action } };

    public static bool ValidServiceName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl);

    /// <summary>Process ids 0 and 4 are the idle and system processes; they cannot be ended and are refused up front.</summary>
    public static bool ValidPid(int pid) => pid > 4;

    private static object? Get(IDictionary map, string key)
    {
        foreach (DictionaryEntry kv in map) if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static string? Text(object? v)
    {
        var t = (v switch { null => null, string s => s, bool or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToString(v, CultureInfo.InvariantCulture), _ => null })?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > MaxText ? t[..MaxText] : t;
    }

    private static long? Whole(object? v) => v switch
    {
        sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(v, CultureInfo.InvariantCulture),
        ulong u when u <= long.MaxValue => (long)u,
        double d when d is >= 0 and < 9e15 => (long)d,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static double? Number(object? v) => v switch
    {
        null => null,
        double d => double.IsFinite(d) ? d : null,
        float f => double.IsFinite(f) ? f : null,
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToDouble(v, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static IEnumerable<IDictionary>? Rows(object? reply)
    {
        if (reply is not IEnumerable seq || reply is string || reply is IDictionary) return null;   // a single map or a word like "timeout" is not a list
        return seq.OfType<IDictionary>();
    }

    /// <summary>The decoded reply to "winservices" (a list of maps), or null when it is not a list.</summary>
    public static List<ServiceDto>? ServicesFromReply(object? reply)
    {
        if (Rows(reply) is not { } rows) return null;
        var list = new List<ServiceDto>();
        foreach (var m in rows)
        {
            var name = Text(Get(m, "name"));
            if (name is null) continue;
            list.Add(new ServiceDto(name, Text(Get(m, "display_name")), Text(Get(m, "status")), Text(Get(m, "start_type")), Text(Get(m, "username"))));
            if (list.Count >= MaxRows) break;
        }
        return list.OrderBy(s => s.DisplayName ?? s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The decoded reply to "procs" (a list of maps), or null when it is not a list.</summary>
    public static List<ProcessDto>? ProcessesFromReply(object? reply)
    {
        if (Rows(reply) is not { } rows) return null;
        var list = new List<ProcessDto>();
        foreach (var m in rows)
        {
            var name = Text(Get(m, "name"));
            if (name is null || Whole(Get(m, "pid")) is not { } pid || pid < 0 || pid > int.MaxValue) continue;
            list.Add(new ProcessDto((int)pid, name, Whole(Get(m, "membytes")), Text(Get(m, "username")), Number(Get(m, "cpu_percent"))));
            if (list.Count >= MaxRows) break;
        }
        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    /// <summary>The agent's answer to a service action: a map {success, errormsg}. Anything else counts as a failure with the text it gave.</summary>
    public static (bool Ok, string Message) ServiceActionResult(object? reply)
    {
        if (reply is IDictionary m)
        {
            var ok = Get(m, "success") is true;
            return ok ? (true, "Done.") : (false, Text(Get(m, "errormsg")) ?? "The agent reported a failure.");
        }
        return reply as string switch
        {
            "ok" => (true, "Done."),
            "timeout" => (false, "The agent did not answer (it may be offline)."),
            "natsdown" => (false, "The NATS server is not reachable."),
            null => (false, "Unexpected reply from the agent."),
            var s => (false, Text(s) ?? "Unexpected reply from the agent."),
        };
    }

    /// <summary>The agent's answer to "killproc": the word "ok" is success, any other text is the reason it failed.</summary>
    public static (bool Ok, string Message) KillResult(object? reply) => reply as string switch
    {
        "ok" => (true, "The process was ended."),
        "timeout" => (false, "The agent did not answer (it may be offline)."),
        "natsdown" => (false, "The NATS server is not reachable."),
        null => (false, "Unexpected reply from the agent."),
        var s => (false, Text(s) ?? "Unexpected reply from the agent."),
    };
}
