using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Event logger, hook health, telemetry status, errors/findings tail, overlay debug.</summary>
internal sealed class DiagnosticsTab
{
    private const int TailLines = 5;

    private readonly DevConsoleContext ctx;

    public DiagnosticsTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        DrawEventLoggerCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawDiscardCaptureCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawTelemetryCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawStreamsCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawHandOverlayDebugCard();
    }

    private void DrawEventLoggerCard()
    {
        using (Theme.BeginCard("diag-log"))
        {
            Theme.SectionHeader("Event logger");
            Theme.Subtle("Logs every UI callback the mahjong addon receives. Useful for reverse-engineering button indices; heavy when on — turn off when done.");
            bool enabled = ctx.Plugin.EventLogger.Enabled;
            if (ImGui.Checkbox("Record clicks to emj-events.log", ref enabled))
            {
                ctx.Plugin.EventLogger.Enabled = enabled;
                if (enabled)
                    ctx.Plugin.EventLogger.OpenLog();
                else
                    ctx.Plugin.EventLogger.CloseLog();
                ctx.LastToast = enabled ? "event logger ON" : "event logger OFF";
            }
            DevHelpers.CopyButton(ctx.Plugin.EventLogger.LogPath, "eventlog");
            ImGui.SameLine(0, 6);
            ImGui.AlignTextToFramePadding();
            Theme.Subtle(ctx.Plugin.EventLogger.LogPath);
        }
    }

    private void DrawDiscardCaptureCard()
    {
        using (Theme.BeginCard("diag-discardhook"))
        {
            Theme.SectionHeader("Discard capture");
            Theme.Subtle("Tracks every discard the moment it commits. Strategy is auto-picked at startup: native asm hook preferred, addon-poll as fallback.");
            var c = ctx.Plugin.DiscardCapture;
            DevHelpers.KeyValueRow("Strategy", c.StrategyName);
            DevHelpers.KeyValueRow("Health", c.Health.ToString());
            DevHelpers.KeyValueRow("Total captured", c.TotalCaptured.ToString());
            DevHelpers.KeyValueRow("Last tile id", c.LastTileId.ToString());
            DevHelpers.KeyValueRow("Log", ctx.Plugin.DiscardCaptureLogger.LogPath);
            DevHelpers.CopyButton(ctx.Plugin.DiscardCaptureLogger.LogPath, "discardlog");
        }
    }

    private void DrawTelemetryCard()
    {
        using (Theme.BeginCard("diag-telemetry"))
        {
            Theme.SectionHeader("Telemetry");
            Theme.Subtle("Anonymous upload of error/finding/memdump files to the project's research endpoint. URL resolves from GitHub at startup.");

            var ep = ctx.Plugin.TelemetryUploader.CurrentEndpoint;
            DevHelpers.KeyValueRow("Endpoint", string.IsNullOrEmpty(ep.UploadUrl) ? "(none)" : ep.UploadUrl);
            DevHelpers.KeyValueRow("Enabled", ep.Enabled.ToString());
            if (!string.IsNullOrEmpty(ep.MinPluginVersion))
                DevHelpers.KeyValueRow("Min plugin ver", ep.MinPluginVersion);

            int pending = ctx.Plugin.TelemetryUploader.CountPending();
            DevHelpers.KeyValueRow("Pending files", pending.ToString());
            if (!string.IsNullOrEmpty(ep.UploadUrl))
                DevHelpers.CopyButton(ep.UploadUrl, "telemetry", $"Copy upload URL to clipboard:\n{ep.UploadUrl}");
        }
    }

    private void DrawStreamsCard()
    {
        using (Theme.BeginCard("diag-streams"))
        {
            Theme.SectionHeader("Errors & findings (tail)");
            Theme.Subtle($"Last {TailLines} lines of today's errors and findings NDJSON. Click Open to inspect the full file.");

            string configDir = Plugin.PluginInterface.GetPluginConfigDirectory();
            string errorsPath = Path.Combine(configDir, "errors", $"errors-{DateTime.UtcNow:yyyyMMdd}.ndjson");
            string findingsPath = Path.Combine(configDir, "findings", $"findings-{DateTime.UtcNow:yyyyMMdd}.ndjson");

            DrawTail("Errors", errorsPath);
            ImGui.Dummy(new Vector2(0, 4));
            DrawTail("Findings", findingsPath);
        }
    }

    private void DrawHandOverlayDebugCard()
    {
        using (Theme.BeginCard("diag-handoverlay"))
        {
            Theme.SectionHeader("Hand overlay debug");
            Theme.Subtle("Outlines every visible node whose dimensions match a tile, in NodeList order — before the row-finder picks the closed-hand subset. Use to spot tiles the size filter missed or extras the Y-cluster heuristic drops.");
            bool on = ctx.Plugin.HandOverlay.DebugDrawAllRects;
            if (ImGui.Checkbox("Outline all detected tile rects", ref on))
                ctx.Plugin.HandOverlay.DebugDrawAllRects = on;
        }
    }

    private static void DrawTail(string label, string path)
    {
        bool exists = File.Exists(path);
        if (exists)
        {
            string folder = Path.GetDirectoryName(path) ?? path;
            DevHelpers.OpenFolderButton(folder, $"tail-{label}", $"Open folder:\n{folder}");
            ImGui.SameLine(0, 3);
            DevHelpers.CopyButton(path, $"tail-{label}", $"Copy path to clipboard:\n{path}");
            ImGui.SameLine(0, 8);
            ImGui.AlignTextToFramePadding();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Faint);
        ImGui.TextUnformatted(exists ? $"({Path.GetFileName(path)})" : "(no file yet)");
        ImGui.PopStyleColor();

        var tail = ReadTail(path, TailLines);
        if (tail.Count == 0)
        {
            Theme.Subtle("  (empty)");
            return;
        }
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
        foreach (var line in tail)
        {
            string truncated = line.Length > 200 ? line[..200] + "…" : line;
            ImGui.TextWrapped("  " + truncated);
        }
        ImGui.PopStyleColor();
    }

    /// <summary>Read the last <paramref name="n"/> lines of an NDJSON file; tolerant of missing files and other readers.</summary>
    private static List<string> ReadTail(string path, int n)
    {
        var lines = new List<string>(n);
        try
        {
            if (!File.Exists(path))
                return lines;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var ring = new Queue<string>(n);
            while (sr.ReadLine() is { } line)
            {
                if (ring.Count == n)
                    ring.Dequeue();
                ring.Enqueue(line);
            }
            lines.AddRange(ring);
        }
        catch { }
        return lines;
    }
}
