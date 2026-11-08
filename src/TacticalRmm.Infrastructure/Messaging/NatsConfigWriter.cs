using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;
using TacticalRmm.Infrastructure.Persistence;

namespace TacticalRmm.Infrastructure.Messaging;

/// <summary>Port tacticalrmm/utils.py::reload_nats — generates nats-rmm.conf (JSON) with the TRMM & AGENTS accounts.</summary>
public sealed class NatsConfigWriter(AppDbContext db, IOptions<TacticalOptions> opt, ILogger<NatsConfigWriter> log) : INatsConfigWriter
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var o = opt.Value;
        var rows = await db.AgentTokens.AsNoTracking()
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { t.Key, u.AgentId })
            .Join(db.Agents, x => x.AgentId, a => (int?)a.Id, (x, a) => new { a.AgentId, x.Key })
            .ToListAsync(ct);

        var agentUsers = rows.Where(r => AgentIdValidator.IsValid(r.AgentId)).Select(r => new Dictionary<string, object>
        {
            ["user"] = r.AgentId,
            ["password"] = r.Key,
            ["permissions"] = new Dictionary<string, object>
            {
                ["publish"] = new { allow = new[] { $"fromagent.{r.AgentId}", $"{r.AgentId}.cmdoutput.>", $"{r.AgentId}.terminal.>" } },
                ["subscribe"] = new { allow = new[] { r.AgentId } },
                ["allow_responses"] = new { expires = o.AllowResponseExpiration },
            }
        }).ToList();

        var config = new Dictionary<string, object>
        {
            ["accounts"] = new Dictionary<string, object>
            {
                ["TRMM"] = new Dictionary<string, object>
                {
                    ["users"] = new[] { new { user = "tacticalrmm", password = o.SecretKey, permissions = new { publish = ">", subscribe = ">" } } },
                    ["mappings"] = new Dictionary<string, string> { ["*"] = "cmd.{{wildcard(1)}}" },
                    ["exports"] = new[] { new { stream = "cmd.*" } },
                    ["imports"] = new object[]
                    {
                        new { stream = new { account = "AGENTS", subject = "fromagent.*" }, to = "{{wildcard(1)}}" },
                        new { stream = new { account = "AGENTS", subject = "_INBOX.>" } },
                        new { stream = new { account = "AGENTS", subject = "*.cmdoutput.>" } },
                        new { stream = new { account = "AGENTS", subject = "*.terminal.>" } },
                    }
                },
                ["AGENTS"] = new Dictionary<string, object>
                {
                    ["users"] = agentUsers,
                    ["mappings"] = new Dictionary<string, string> { ["*"] = "fromagent.{{wildcard(1)}}" },
                    ["exports"] = new[] { new { stream = "fromagent.*" }, new { stream = "_INBOX.>" }, new { stream = "*.cmdoutput.>" }, new { stream = "*.terminal.>" } },
                    ["imports"] = new[] { new { stream = new { account = "TRMM", subject = "cmd.*" }, to = "{{wildcard(1)}}" } },
                }
            },
            ["max_payload"] = 67108864,
            ["host"] = o.NatsStdHost,
            ["port"] = o.NatsStdPort,
            ["websocket"] = new { host = o.NatsWsHost, port = o.NatsWsPort, no_tls = true },
        };

        await Gate.WaitAsync(ct);
        try
        {
            var tmp = o.NatsConfPath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(config), ct);
            File.Move(tmp, o.NatsConfPath, overwrite: true);
        }
        finally { Gate.Release(); }

        try
        {
            await Task.Delay(500, ct);
            using var p = Process.Start(new ProcessStartInfo(o.NatsServerBin, "-signal reload") { RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is not null) await p.WaitForExitAsync(ct);
        }
        catch (Exception e) { log.LogWarning(e, "nats-server reload failed (ignore if nats runs in a separate container)"); }
    }
}
