using TacticalRmm.Application.Dtos;
using TacticalRmm.Domain;
using TacticalRmm.Domain.Entities;

namespace TacticalRmm.Application.Logic;

/// <summary>Port dari CheckResult.handle_check (checks/models.py). Murni, tanpa I/O, mudah diuji.</summary>
public static class CheckEvaluator
{
    public static int? HistorySample(Check check, CheckResultPayload d) => check.CheckType switch
    {
        CheckType.CpuLoad or CheckType.Memory => d.Percent,
        CheckType.DiskSpace when d.Exists == true => 100 - (int)Math.Round(d.PercentUsed ?? 0, MidpointRounding.ToEven),
        _ => null
    };

    public static void Apply(Check check, CheckResult r, CheckResultPayload d)
    {
        switch (check.CheckType)
        {
            case CheckType.CpuLoad:
            case CheckType.Memory:
            {
                r.History.Add(d.Percent ?? 0);
                if (r.History.Count > 15) r.History = r.History.Skip(r.History.Count - 15).ToList();
                var avg = (int)r.History.Average();
                r.MoreInfo = $"Average {(check.CheckType == CheckType.Memory ? "Memory Usage" : "CPU Load")}: {avg}%";
                if (check.ErrorThreshold is > 0 && avg > check.ErrorThreshold) Fail(r, AlertSeverity.Error);
                else if (check.WarningThreshold is > 0 && avg > check.WarningThreshold) Fail(r, AlertSeverity.Warning);
                else r.Status = CheckStatus.Passing;
                break;
            }
            case CheckType.DiskSpace:
            {
                if (d.Exists == true)
                {
                    var free = 100 - (int)Math.Round(d.PercentUsed ?? 0, MidpointRounding.ToEven);
                    if (check.ErrorThreshold is > 0 && free < check.ErrorThreshold) Fail(r, AlertSeverity.Error);
                    else if (check.WarningThreshold is > 0 && free < check.WarningThreshold) Fail(r, AlertSeverity.Warning);
                    else r.Status = CheckStatus.Passing;
                    r.MoreInfo = d.MoreInfo;
                }
                else
                {
                    Fail(r, AlertSeverity.Error);
                    r.MoreInfo = $"Disk {check.Disk} does not exist";
                }
                break;
            }
            case CheckType.Script:
            {
                r.Stdout = d.Stdout; r.Stderr = d.Stderr; r.Retcode = d.Retcode;
                r.ExecutionTime = (d.Runtime ?? 0).ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
                var rc = d.Retcode ?? 0;
                if (check.InfoReturnCodes.Contains(rc)) Fail(r, AlertSeverity.Info);
                else if (check.WarningReturnCodes.Contains(rc)) Fail(r, AlertSeverity.Warning);
                else if (check.SuccessReturnCodes.Contains(rc)) r.Status = CheckStatus.Passing;
                else if (rc != 0) Fail(r, AlertSeverity.Error);
                else r.Status = CheckStatus.Passing;
                break;
            }
            case CheckType.Ping:
                r.Status = d.Status ?? CheckStatus.Failing;
                r.MoreInfo = d.Output;
                if (r.Status == CheckStatus.Failing) r.AlertSeverity = check.AlertSeverity;
                break;
            default: // winsvc & eventlog: agent mengirim status final
                r.Status = d.Status ?? CheckStatus.Failing;
                r.MoreInfo = d.MoreInfo;
                if (r.Status == CheckStatus.Failing) r.AlertSeverity = check.AlertSeverity;
                break;
        }

        if (r.Status == CheckStatus.Passing) { r.FailCount = 0; r.AlertSeverity = null; }
        else r.FailCount++;
        r.LastRun = DateTime.UtcNow;
    }

    private static void Fail(CheckResult r, string severity) { r.Status = CheckStatus.Failing; r.AlertSeverity = severity; }

    /// <summary>Alert hanya dibuat setelah gagal berturut-turut sebanyak fails_b4_alert.</summary>
    public static bool ShouldAlert(Check check, CheckResult r) =>
        r.Status == CheckStatus.Failing && r.FailCount >= Math.Max(1, check.FailsB4Alert);

    public static bool IsDue(Check check, CheckResult? r, int agentInterval, DateTime nowUtc) =>
        r?.LastRun is null || r.LastRun < nowUtc.AddSeconds(-(check.RunInterval > 0 ? check.RunInterval : agentInterval));
}
