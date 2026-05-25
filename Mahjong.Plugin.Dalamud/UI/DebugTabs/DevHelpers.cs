using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Shared ImGui primitives for the developer console.</summary>
internal static class DevHelpers
{
    /// <summary>Two-column "label — value" row with a 140px gutter.</summary>
    public static void KeyValueRow(string key, string value)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextUnformatted(key);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, 140 - ImGui.CalcTextSize(key).X));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }

    /// <summary>Tinted full-width status banner under the active tab.</summary>
    public static void DrawToast(string text)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        float w = ImGui.GetContentRegionAvail().X;
        var size = new Vector2(w, 30);
        var min = pos;
        var max = min + size;
        dl.AddRectFilled(min, max, Theme.Pack(Theme.Info, 0.15f), 6f);
        dl.AddRect(min, max, Theme.Pack(Theme.Info, 0.55f), 6f, ImDrawFlags.None, 1f);
        var ts = ImGui.CalcTextSize(text);
        var tp = min + new Vector2(12, (size.Y - ts.Y) * 0.5f);
        dl.AddText(tp, Theme.Pack(Theme.Info), text);
        ImGui.Dummy(size);
    }

    /// <summary>RAII wrapper around <see cref="ImGui.BeginDisabled()"/>.</summary>
    public static ImDisable Disable(bool disabled) => new(disabled);

    internal readonly struct ImDisable : IDisposable
    {
        private readonly bool active;
        public ImDisable(bool disable) { active = disable; if (disable) ImGui.BeginDisabled(); }
        public void Dispose() { if (active) ImGui.EndDisabled(); }
    }

    /// <summary>Open a directory in the OS file explorer; swallows failures.</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch { }
    }

    private static readonly Dictionary<string, double> recentlyCopied = new();
    private const double FeedbackSeconds = 1.2;

    private static double NowSeconds()
        => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

    /// <summary>Icon-only action button used by Copy/Open helpers — transparent bg, accent-tinted hover, ~24px tall.</summary>
    private static bool IconActionButton(FontAwesomeIcon icon, string id, Vector4 iconColor, out bool hovered)
    {
        bool clicked;
        bool wasHovered;
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 1f, 1f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Fade(Theme.Accent, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Fade(Theme.Accent, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            clicked = ImGui.Button($"{icon.ToIconString()}##{id}");
            wasHovered = ImGui.IsItemHovered();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        hovered = wasHovered;
        return clicked;
    }

    /// <summary>Copies <paramref name="textToCopy"/> to the clipboard; flashes a check icon for ~1.2s on click.</summary>
    public static void CopyButton(string textToCopy, string id, string? tooltip = null)
    {
        string key = $"copy-{id}";
        bool justCopied = recentlyCopied.TryGetValue(key, out var until) && NowSeconds() < until;
        var icon = justCopied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy;
        var color = justCopied ? Theme.Accent : Theme.Muted;
        bool clicked = IconActionButton(icon, key, color, out bool hovered);
        if (hovered)
            ImGui.SetTooltip(tooltip ?? $"Copy to clipboard:\n{textToCopy}");
        if (clicked)
        {
            ImGui.SetClipboardText(textToCopy);
            recentlyCopied[key] = NowSeconds() + FeedbackSeconds;
        }
    }

    /// <summary>Opens <paramref name="folderPath"/> in the OS file explorer.</summary>
    public static void OpenFolderButton(string folderPath, string id, string? tooltip = null)
    {
        bool clicked = IconActionButton(FontAwesomeIcon.FolderOpen, $"open-{id}", Theme.Muted, out bool hovered);
        if (hovered)
            ImGui.SetTooltip(tooltip ?? $"Open folder:\n{folderPath}");
        if (clicked)
            OpenFolder(folderPath);
    }

    /// <summary>Copies the absolute path of a dump file under the plugin config dir.</summary>
    public static void CopyPathButton(string fileName, string buttonId)
    {
        string path = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), fileName);
        CopyButton(path, buttonId, $"Copy path to clipboard:\n{path}");
    }

    /// <summary>Parse "0x123" or "123" hex into int.</summary>
    public static bool TryParseHex(string s, out int value)
    {
        s = s?.Trim() ?? string.Empty;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        return int.TryParse(s,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}
