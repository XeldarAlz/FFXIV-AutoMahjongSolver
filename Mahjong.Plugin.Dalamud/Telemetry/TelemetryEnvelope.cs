using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Dalamud.Game;

namespace Mahjong.Plugin.Dalamud.Telemetry;

public sealed record TelemetryEnvelope(
    Guid InstallId,
    string PluginVersion,
    string PluginHash,
    string GameVersion,
    string ClientRegion,
    string OsPlatform,
    int SchemaVersion)
{
    public const int CurrentSchemaVersion = 1;

    public static TelemetryEnvelope Build(Guid installId, ClientLanguage clientLanguage)
    {
        return new TelemetryEnvelope(
            InstallId: installId,
            PluginVersion: SafeGet(GetPluginVersion, "0.0.0"),
            PluginHash: SafeGet(GetPluginAssemblyHash, "unknown"),
            GameVersion: SafeGet(GetGameVersion, "unknown"),
            ClientRegion: clientLanguage.ToString(),
            OsPlatform: Environment.OSVersion.Platform.ToString(),
            SchemaVersion: CurrentSchemaVersion);
    }

    private static string SafeGet(Func<string> get, string fallback)
    {
        try
        { return get() ?? fallback; }
        catch { return fallback; }
    }

    private static string GetPluginVersion() =>
        typeof(TelemetryEnvelope).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TelemetryEnvelope).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static string GetPluginAssemblyHash()
    {
        var path = typeof(TelemetryEnvelope).Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "unknown";
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).Substring(0, 16);
    }

    /// <summary>FFXIV's real build lives in ffxivgame.ver next to the exe — the PE header FileVersion is an unset "1, 0, 0, 0" placeholder.</summary>
    private static string GetGameVersion()
    {
        var module = Process.GetCurrentProcess().MainModule;
        var exePath = module?.FileName;
        if (!string.IsNullOrEmpty(exePath))
        {
            var dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var verPath = Path.Combine(dir, "ffxivgame.ver");
                if (File.Exists(verPath))
                {
                    var content = File.ReadAllText(verPath).Trim();
                    if (!string.IsNullOrEmpty(content))
                        return content;
                }
            }
        }
        return module?.FileVersionInfo.FileVersion
            ?? module?.FileVersionInfo.ProductVersion
            ?? "unknown";
    }
}
