using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TacticalRmm.Application.Abstractions;
using TacticalRmm.Application.Dtos;
using TacticalRmm.Application.Logic;

namespace TacticalRmm.Infrastructure.Services;

/// <summary>Port core/utils.py: get_mesh_ws_url + get_mesh_device_id + get_meshagent_url + download_mesh_agent.</summary>
public sealed class MeshService(IHttpClientFactory http, IOptions<TacticalOptions> opt) : IMeshService
{
    private MeshOptions M => opt.Value.Mesh;

    public async Task<byte[]> DownloadAgentAsync(int ident, string plat, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(M.TokenKey) || string.IsNullOrEmpty(M.Username))
            throw new MeshUnavailableException("Mesh belum dikonfigurasi (Tactical:Mesh)");

        try
        {
            var meshId = await GetDeviceGroupIdAsync(ct);
            var baseUrl = M.UseExternal ? M.Site : $"http://127.0.0.1:{M.LocalPort}";
            var url = MeshUrls.AgentDownload(baseUrl, ident, plat, meshId);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var client = http.CreateClient("mesh"); // TLS tetap divalidasi; tidak ada verify=False seperti Django
            using var resp = await client.GetAsync(url, cts.Token);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new MeshUnavailableException($"Unable to download mesh agent: {e.Message}", e);
        }
    }

    private async Task<string> GetDeviceGroupIdAsync(CancellationToken ct)
    {
        var token = MeshAuth.CreateAuthToken(M.Username, M.TokenKey);
        var uri = new Uri(MeshUrls.ControlSocket(M.Site, M.UseExternal, M.LocalPort, token));

        using var ws = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        await ws.ConnectAsync(uri, cts.Token);
        await ws.SendAsync(Encoding.UTF8.GetBytes("{\"action\":\"meshes\",\"responseid\":\"meshctrl\"}"),
            WebSocketMessageType.Text, true, cts.Token);

        var buf = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Mesh menutup koneksi");
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 100 * 1024 * 1024) throw new InvalidOperationException("Pesan Mesh terlalu besar");
            } while (!r.EndOfMessage);

            using var doc = JsonDocument.Parse(ms.ToArray());
            var root = doc.RootElement;
            if (!root.TryGetProperty("action", out var action) || action.GetString() != "meshes") continue;

            foreach (var m in root.GetProperty("meshes").EnumerateArray())
            {
                if (m.GetProperty("name").GetString() != M.DeviceGroup) continue;
                var id = m.GetProperty("_id").GetString() ?? "";
                var i = id.IndexOf("mesh//", StringComparison.Ordinal);
                if (i >= 0) return id[(i + 6)..];
            }
            throw new InvalidOperationException($"Device group '{M.DeviceGroup}' tidak ditemukan di Mesh");
        }
        throw new InvalidOperationException("Koneksi Mesh terputus sebelum daftar grup diterima");
    }
}
