using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Snap/autosnap/capture/variant-dump — the bug-report capture surface.</summary>
internal sealed class BugReportTab
{
    private readonly DevConsoleContext ctx;
    private string snapLabel = "report";
    private string captureLabel = "click";

    public BugReportTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        var cmd = ctx.Plugin.MjAutoCommand;
        string configDir = Plugin.PluginInterface.GetPluginConfigDirectory();

        using (Theme.BeginCard("br-snap"))
        {
            Theme.SectionHeader("Snap (one-shot memory dump)");
            Theme.Subtle("Saves the addon + agent memory to a labeled file. Use this when an issue asks for a one-off snapshot. Label = letters, digits, dash, underscore.");

            if (!IsValidLabel(snapLabel))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
                ImGui.TextUnformatted("Label must be [a-zA-Z0-9_-] only.");
                ImGui.PopStyleColor();
            }

            ImGui.SetNextItemWidth(220);
            ImGui.InputText("##snaplbl", ref snapLabel, 64);
            ImGui.SameLine(0, 8);
            using (DevHelpers.Disable(!IsValidLabel(snapLabel)))
            {
                if (ImGui.Button("Snap"))
                {
                    var label = snapLabel;
                    cmd.HandleSnap(label);
                    ctx.LastToast = $"snap '{label}' queued → see chat for path";
                }
            }
            Theme.Subtle($"Writes snap-LABEL-ts.txt to plugin config dir.");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("br-autosnap"))
        {
            Theme.SectionHeader("Autosnap (continuous capture)");
            Theme.Subtle("Saves a fresh dump every time the game state changes. Use when you can't predict when the bug will trigger. Caps at 500 files; identical states are dropped.");
            bool on = cmd.IsAutoSnapOn;

            DevHelpers.KeyValueRow("State", on ? "ON" : "OFF");
            DevHelpers.KeyValueRow("Count", $"{cmd.AutoSnapCount} / {cmd.AutoSnapMaxCountValue}");

            ImGui.Dummy(new Vector2(0, 2));
            if (on)
            {
                if (ImGui.Button("Stop autosnap"))
                {
                    cmd.HandleAutoSnap("off");
                    ctx.LastToast = "autosnap OFF";
                }
            }
            else
            {
                if (ImGui.Button("Start autosnap"))
                {
                    cmd.HandleAutoSnap("on");
                    ctx.LastToast = "autosnap ON — files: snap-auto-NNN-<ts>.txt";
                }
            }
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("br-capture"))
        {
            Theme.SectionHeader("Capture (arm-and-click)");
            Theme.Subtle("Records the very next click you make in the mahjong UI with a label. Type a name, press Arm, then click in-game. Auto-disarms after one click or 60s.");
            var pending = ctx.Plugin.EventLogger.PendingCaptureLabel;

            if (pending is not null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                ImGui.TextUnformatted($"ARMED: '{pending}' — click the action in-game");
                ImGui.PopStyleColor();
                ImGui.Dummy(new Vector2(0, 2));
                if (ImGui.Button("Disarm"))
                {
                    ctx.Plugin.EventLogger.DisarmCapture();
                    ctx.LastToast = $"capture disarmed (was: {pending})";
                }
            }
            else
            {
                ImGui.SetNextItemWidth(220);
                ImGui.InputText("##caplbl", ref captureLabel, 64);
                ImGui.SameLine(0, 8);
                bool armBlocked = !IsValidLabel(captureLabel) || ctx.Plugin.Configuration.AutomationArmed;
                using (DevHelpers.Disable(armBlocked))
                {
                    if (ImGui.Button("Arm"))
                    {
                        cmd.HandleCapture(captureLabel);
                        ctx.LastToast = $"capture armed: '{captureLabel}' — auto-disarms after one click or 60s";
                    }
                }
                if (ctx.Plugin.Configuration.AutomationArmed)
                    Theme.Subtle("Auto-play is ON — disarm automation first or its dispatch will race your click.");
            }
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("br-variant"))
        {
            Theme.SectionHeader("Variant dump (new-client report)");
            Theme.Subtle("Run this when the plugin can't read your hand or board after a game patch. Writes one file to attach when reporting a new client variant.");
            if (ImGui.Button("Dump variant"))
            {
                cmd.DumpVariant();
                ctx.LastToast = "variant dump queued → emj-variant-dump.txt";
            }
            ImGui.SameLine(0, 6);
            DevHelpers.CopyPathButton("emj-variant-dump.txt", "variant");
            Theme.Subtle("Attach emj-variant-dump.txt when reporting a new client variant.");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("br-folder"))
        {
            Theme.SectionHeader("Files");
            Theme.Subtle("Open the folder containing every dump file. Drag-and-drop into a GitHub issue to attach.");

            DrawDirRow("Plugin config", configDir, "cfgdir");
            string findingsDir = Path.Combine(configDir, "findings");
            if (Directory.Exists(findingsDir))
                DrawDirRow("Findings",      findingsDir, "findingsdir");
        }

        ImGui.Dummy(new Vector2(0, 4));
        DrawRecentDumpsCard(configDir);
    }

    /// <summary>Icon pair (open + copy) followed by an aligned "label: path" line for a directory.</summary>
    private static void DrawDirRow(string label, string path, string id)
    {
        DevHelpers.OpenFolderButton(path, id, $"Open folder:\n{path}");
        ImGui.SameLine(0, 3);
        DevHelpers.CopyButton(path, id, $"Copy path to clipboard:\n{path}");
        ImGui.SameLine(0, 8);
        ImGui.AlignTextToFramePadding();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextUnformatted($"{label}:");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Faint);
        ImGui.TextWrapped(path);
        ImGui.PopStyleColor();
    }

    /// <summary>Last 10 generated dump files; click to reveal in explorer or copy path.</summary>
    private static void DrawRecentDumpsCard(string configDir)
    {
        using (Theme.BeginCard("br-recent"))
        {
            Theme.SectionHeader("Recent dumps");
            Theme.Subtle("Most recently produced dump files. Open reveals the file's folder; Copy puts the full path on your clipboard.");

            var files = RecentDumps(configDir, 10);
            if (files.Count == 0)
            {
                Theme.Subtle("(no dumps yet)");
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                var (path, when) = files[i];
                string name = Path.GetFileName(path);
                string age = FormatAge(DateTime.UtcNow - when);
                string folder = Path.GetDirectoryName(path) ?? configDir;

                DevHelpers.OpenFolderButton(folder, $"rd{i}", $"Reveal in folder:\n{folder}");
                ImGui.SameLine(0, 3);
                DevHelpers.CopyButton(path, $"rd{i}", $"Copy path to clipboard:\n{path}");
                ImGui.SameLine(0, 8);

                ImGui.AlignTextToFramePadding();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
                ImGui.TextUnformatted(name);
                ImGui.PopStyleColor();
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Faint);
                ImGui.TextUnformatted($"  {age} ago");
                ImGui.PopStyleColor();
            }
        }
    }

    private static List<(string Path, DateTime WrittenUtc)> RecentDumps(string configDir, int max)
    {
        var result = new List<(string, DateTime)>();
        try
        {
            if (!Directory.Exists(configDir))
                return result;
            string[] patterns = { "snap-*.txt", "emj-*.txt" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pat in patterns)
            {
                foreach (var f in Directory.EnumerateFiles(configDir, pat, SearchOption.TopDirectoryOnly))
                {
                    if (!seen.Add(f))
                        continue;
                    result.Add((f, File.GetLastWriteTimeUtc(f)));
                }
            }
            result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            if (result.Count > max)
                result.RemoveRange(max, result.Count - max);
        }
        catch { }
        return result;
    }

    private static string FormatAge(TimeSpan d)
    {
        if (d.TotalSeconds < 60)
            return $"{(int)d.TotalSeconds}s";
        if (d.TotalMinutes < 60)
            return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24)
            return $"{(int)d.TotalHours}h";
        return $"{(int)d.TotalDays}d";
    }

    private static bool IsValidLabel(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        foreach (var c in s)
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        return true;
    }
}
