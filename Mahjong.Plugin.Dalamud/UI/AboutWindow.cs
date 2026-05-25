using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Mahjong.Plugin.Dalamud.UI;

public sealed class AboutWindow : Window, IDisposable
{
    private const string RepoUrl = "https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver";
    private const string IssuesUrl = "https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/issues";
    private const string DiscussionsUrl = "https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/discussions";
    private const string SecurityUrl = "https://github.com/XeldarAlz/FFXIV-DomanMahjongSolver/security/advisories/new";
    private const string Author = "XeldarAlz";

    private static readonly Vector4 GreetingColor = new(0.96f, 0.84f, 0.62f, 1.00f);
    private static readonly Vector4 GreetingShimmer = new(1.00f, 0.94f, 0.78f, 1.00f);
    private static readonly string[] GreetingParagraphs =
    {
        "Hello there! I'm a solo developer building FFXIV automation plugins in my free time.",
        "If this one made your day a little easier, the best way to support the project is to share it with other players.",
        "I'd love to hear from you too: bug reports, feature requests, and general feedback are all welcome on GitHub Discussions.",
        "Thanks for trying it out, and have fun out there!",
    };

    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITextureProvider textureProvider;
    private readonly Dictionary<string, float> linkHoverPulse = new();

    public AboutWindow(IPluginLog log, IDalamudPluginInterface pluginInterface, ITextureProvider textureProvider)
        : base("Doman Mahjong Solver: About###domanmahjong-about")
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(textureProvider);
        this.log = log;
        this.pluginInterface = pluginInterface;
        this.textureProvider = textureProvider;

        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(560, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 360),
            MaximumSize = new Vector2(900, 2000),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        using var _s = Theme.PushWindowStyle();

        DrawIcon();
        DrawHeader();
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 4));
        DrawDetailsTable();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));
        DrawShimmerGreeting();
    }

    private void DrawIcon()
    {
        var iconSize = 96f;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > iconSize)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - iconSize) * 0.5f);

        var iconPath = Path.Combine(
            pluginInterface.AssemblyLocation.DirectoryName ?? "",
            "Images", "Icon.png");
        if (!File.Exists(iconPath))
        {
            ImGui.Dummy(new Vector2(iconSize, iconSize));
            return;
        }

        var tex = textureProvider.GetFromFile(iconPath).GetWrapOrDefault();
        if (tex == null)
        {
            ImGui.Dummy(new Vector2(iconSize, iconSize));
            return;
        }

        var alpha = 0.85f + 0.15f * Theme.Pulse(2.0f, 0f, 1f);
        ImGui.Image(tex.Handle, new Vector2(iconSize, iconSize), Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, alpha));
        ImGui.Dummy(new Vector2(0, 4));
    }

    private static void DrawHeader()
    {
        var version = typeof(AboutWindow).Assembly.GetName().Version?.ToString() ?? "?";
        var availWidth = ImGui.GetContentRegionAvail().X;
        var label = $"v {version}";
        var textWidth = ImGui.CalcTextSize(label).X;
        if (availWidth > textWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - textWidth) * 0.5f);

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    private void DrawDetailsTable()
    {
        if (!ImGui.BeginTable("##about_table", 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.PadOuterX))
            return;

        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthStretch);

        DrawTextRow("Author", Author);
        DrawLinkRow("GitHub", RepoUrl);
        DrawLinkRow("Report a bug", IssuesUrl);
        DrawLinkRow("Discussions", DiscussionsUrl);
        DrawLinkRow("Security disclosure", SecurityUrl);

        ImGui.EndTable();
    }

    private static void DrawShimmerGreeting()
    {
        ImGui.PushTextWrapPos(0f);
        var availWidth = ImGui.GetContentRegionAvail().X;
        var bandWidth = 140f;
        var dl = ImGui.GetWindowDrawList();

        const int loopMs = 5000;
        const int staggerMs = 800;
        var tick = Environment.TickCount;

        for (int i = 0; i < GreetingParagraphs.Length; i++)
        {
            var para = GreetingParagraphs[i];
            var startPos = ImGui.GetCursorScreenPos();

            ImGui.PushStyleColor(ImGuiCol.Text, GreetingColor);
            ImGui.TextUnformatted(para);
            ImGui.PopStyleColor();
            var endPos = ImGui.GetCursorScreenPos();

            int mod = (tick - i * staggerMs) % loopMs;
            if (mod < 0) mod += loopMs;
            float phase = mod / (float)loopMs;
            float bandCenter = startPos.X - bandWidth + phase * (availWidth + bandWidth * 2f);

            dl.PushClipRect(
                new Vector2(bandCenter - bandWidth * 0.5f, startPos.Y),
                new Vector2(bandCenter + bandWidth * 0.5f, endPos.Y),
                true);
            ImGui.SetCursorScreenPos(startPos);
            ImGui.PushStyleColor(ImGuiCol.Text, GreetingShimmer);
            ImGui.TextUnformatted(para);
            ImGui.PopStyleColor();
            ImGui.SetCursorScreenPos(endPos);
            dl.PopClipRect();

            if (i < GreetingParagraphs.Length - 1)
                ImGui.Dummy(new Vector2(0, ImGui.GetTextLineHeight() * 0.5f));
        }

        ImGui.PopTextWrapPos();
    }

    private static void DrawTextRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.TableSetColumnIndex(1);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
    }

    private void DrawLinkRow(string label, string url)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        ImGui.TableSetColumnIndex(1);

        linkHoverPulse.TryGetValue(url, out var pulse);
        var color = Vector4.Lerp(Theme.Info, Theme.Header, pulse * 0.55f);

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X);
        ImGui.TextUnformatted(url);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        var hovered = ImGui.IsItemHovered();
        linkHoverPulse[url] = hovered
            ? MathF.Min(pulse + 0.15f, 1f)
            : MathF.Max(pulse - 0.10f, 0f);

        if (!hovered)
            return;

        ImGui.BeginTooltip();
        ImGui.TextUnformatted("Click to open · right-click to copy");
        ImGui.EndTooltip();

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            OpenInBrowser(url);
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            ImGui.SetClipboardText(url);
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[Mahjong.Plugin.Dalamud] failed to launch browser for {0}, copied to clipboard instead", url);
            ImGui.SetClipboardText(url);
        }
    }
}
