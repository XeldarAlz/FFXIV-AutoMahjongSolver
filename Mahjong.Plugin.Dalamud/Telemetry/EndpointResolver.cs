using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed class EndpointResolver
{
    public const string EmbeddedFallbackUrl =
        "https://mahjong-telemetry.xeldaralz.workers.dev/v1/upload";

    public const string ConfigUrl =
        "https://raw.githubusercontent.com/XeldarAlz/FFXIV-MahjongAI/main/server/telemetry-endpoint.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Times out after 5 s — startup must not block on GitHub. Failure returns the embedded-fallback endpoint with telemetry enabled.</summary>
    public static async Task<TelemetryEndpoint> ResolveAsync(
        HttpClient http, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var resp = await http.GetAsync(ConfigUrl, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            var parsed = await JsonSerializer.DeserializeAsync<TelemetryEndpoint>(
                stream, JsonOpts, cts.Token).ConfigureAwait(false);

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.UploadUrl))
                return Fallback();
            return parsed;
        }
        catch
        {
            return Fallback();
        }
    }

    private static TelemetryEndpoint Fallback() =>
        new(UploadUrl: EmbeddedFallbackUrl, Enabled: true, MinPluginVersion: null);
}

public sealed record TelemetryEndpoint(
    [property: JsonPropertyName("upload_url")] string UploadUrl,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("min_plugin_version")] string? MinPluginVersion);
