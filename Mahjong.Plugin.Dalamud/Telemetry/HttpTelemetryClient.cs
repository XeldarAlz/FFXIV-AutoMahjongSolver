using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed class HttpTelemetryClient
{
    private readonly HttpClient http;
    private readonly TelemetryEnvelope envelope;
    private readonly IPluginLog log;

    public HttpTelemetryClient(HttpClient http, TelemetryEnvelope envelope, IPluginLog log)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(log);
        this.http = http;
        this.envelope = envelope;
        this.log = log;
    }

    public async Task<bool> UploadAsync(
        string endpoint, string stream, string payloadPath, CancellationToken ct)
    {
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, endpoint);
            msg.Headers.Add("X-Install-Id", envelope.InstallId.ToString("D"));
            msg.Headers.Add("X-Plugin-Version", envelope.PluginVersion);
            msg.Headers.Add("X-Plugin-Hash", envelope.PluginHash);
            msg.Headers.Add("X-Game-Version", envelope.GameVersion);
            msg.Headers.Add("X-Client-Region", envelope.ClientRegion);
            msg.Headers.Add("X-Os-Platform", envelope.OsPlatform);
            msg.Headers.Add("X-Schema-Version", envelope.SchemaVersion.ToString());
            msg.Headers.Add("X-Stream", stream);
            msg.Headers.Add("X-Filename", Path.GetFileName(payloadPath));

            // Pre-gzip to set Content-Length so the server can reject oversize uploads pre-body.
            var compressed = await ReadAndCompressAsync(payloadPath, ct).ConfigureAwait(false);
            var content = new ByteArrayContent(compressed);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentEncoding.Add("gzip");
            msg.Content = content;

            using var resp = await http.SendAsync(msg, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return true;

            log.Warning(
                $"[Telemetry] upload failed: stream={stream} file={Path.GetFileName(payloadPath)} " +
                $"status={(int)resp.StatusCode}");
            return false;
        }
        catch (OperationCanceledException)
        {
            log.Debug($"[Telemetry] upload canceled: {Path.GetFileName(payloadPath)}");
            return false;
        }
        catch (Exception ex)
        {
            log.Warning($"[Telemetry] upload exception: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<byte[]> ReadAndCompressAsync(string path, CancellationToken ct)
    {
        await using var src = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        using var ms = new MemoryStream();
        await using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            await src.CopyToAsync(gz, ct).ConfigureAwait(false);
        }
        return ms.ToArray();
    }
}
