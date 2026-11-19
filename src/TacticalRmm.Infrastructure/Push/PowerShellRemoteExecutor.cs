using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Infrastructure.Push;

/// <summary>
/// Runs the install script on one remote computer through PowerShell remoting (WinRM, Negotiate). One PowerShell process per target, so a hung
/// target can be killed. The password is handed over on standard input as JSON: it never appears on a command line or in the environment.
/// </summary>
public sealed class PowerShellRemoteExecutor(IOptions<TacticalOptions> opt) : IRemoteExecutor
{
    // Runs inside the PowerShell child. Reads {host,user,pass,script,ssl,timeout} from stdin and prints exactly one line: NTRMM:{json}.
    private const string Driver = """
        $ErrorActionPreference='Stop'
        $in=[Console]::In.ReadToEnd() | ConvertFrom-Json
        function Emit($s,$m){ [Console]::Out.WriteLine('NTRMM:' + (@{status=$s;message=$m} | ConvertTo-Json -Compress)) }
        try {
          # PSWSMan 3.x: switch the WSMan client on for this session. (2.x installed libraries at build time and has no such command.)
          Import-Module PSWSMan -ErrorAction SilentlyContinue
          if(Get-Command Enable-PSWSMan -ErrorAction SilentlyContinue){ try { Enable-PSWSMan -Force -ErrorAction Stop } catch { Enable-PSWSMan -ErrorAction Stop } }
          $cred=New-Object System.Management.Automation.PSCredential($in.user,(ConvertTo-SecureString $in.pass -AsPlainText -Force))
          # Not New-PSSessionOption: on Linux it lacks -OpenTimeout and others. The .NET class has the same properties everywhere.
          $so=New-Object System.Management.Automation.Remoting.PSSessionOption
          $so.OpenTimeout=[TimeSpan]::FromSeconds(30); $so.OperationTimeout=[TimeSpan]::FromSeconds([int]$in.timeout); $so.NoMachineProfile=$true
          $p=@{ComputerName=$in.host;Credential=$cred;Authentication='Negotiate';SessionOption=$so;ScriptBlock=[scriptblock]::Create($in.script);ErrorAction='Stop'}
          if($in.ssl){ $p.UseSSL=$true }
          $r=Invoke-Command @p
          Emit ([string]($r | Select-Object -Last 1)) ''
        } catch { Emit 'failed' ($_.Exception.Message) }
        """;

    public async Task<RemoteRunResult> RunAsync(RemoteRunRequest r, CancellationToken ct = default)
    {
        var o = opt.Value.Push;
        var exe = !string.IsNullOrWhiteSpace(o.PowerShellPath) ? o.PowerShellPath : OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Driver)) }) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try { proc.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new RemoteRunResult(PushInstall.Failed, $"PowerShell ('{exe}') could not be started on the server: {e.Message}. See the README, section 'Push install'.", PushInstall.CatError);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(r.TimeoutSeconds + 60));
        try
        {
            var payload = JsonSerializer.Serialize(new { host = r.Host, user = r.Username, pass = r.Password, script = r.Script, ssl = r.UseSsl, timeout = r.TimeoutSeconds });
            await proc.StandardInput.WriteAsync(payload.AsMemory(), timeout.Token);
            proc.StandardInput.Close();
            var stdout = proc.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = proc.StandardError.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
            return Parse(await stdout, await stderr, r);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            return new RemoteRunResult(PushInstall.Failed, "Timed out waiting for the computer.", PushInstall.CatTimeout);
        }
    }

    private static RemoteRunResult Parse(string stdout, string stderr, RemoteRunRequest r)
    {
        var line = stdout.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("NTRMM:", StringComparison.Ordinal));
        if (line is null)
            return new RemoteRunResult(PushInstall.Failed, PushInstall.Scrub(string.IsNullOrWhiteSpace(stderr) ? "PowerShell returned no result." : stderr, r.Password, r.Username), PushInstall.CatError);
        try
        {
            using var doc = JsonDocument.Parse(line["NTRMM:".Length..]);
            var status = doc.RootElement.GetProperty("status").GetString() ?? PushInstall.Failed;
            var msg = PushInstall.Scrub(doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null, r.Password);
            if (status is PushInstall.Installed or PushInstall.AlreadyInstalled) return new RemoteRunResult(status, null, PushInstall.CatOk);
            return new RemoteRunResult(PushInstall.Failed, msg, PushInstall.Categorize(msg));
        }
        catch (JsonException) { return new RemoteRunResult(PushInstall.Failed, "Unreadable result from PowerShell.", PushInstall.CatError); }
    }
}
