using AutoMahjongSolver.Core;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

/// <summary>
/// The auto-play cockpit. Deliberately shows no tiles: the bot is acting, so the panel reads like an
/// autopilot — one status ring saying what it is doing now, a session scoreboard, and a short log.
/// </summary>
internal static class AutoPanel
{
    private const float RingRadius = 44f;
    private const float RingThickness = 3.5f;
    private const float StatTileHeight = 56f;
    private const int LogRows = 5;

    private readonly record struct PhaseVisual(
        FontAwesomeIcon Icon, Vector4 Accent, string Title, string Sub, double SweepPeriod, float Energy);

    public static void Draw(AutoPlayController auto, LiveBoard board)
    {
        DrawAutopilot(VisualFor(board));

        Styling.VSpace(14);
        DrawSessionStats(auto.Stats);

        if (auto.Stats.Recent.Count > 0)
        {
            Styling.VSpace(12);
            DrawSessionLog(auto.Stats);
        }
    }

    private static PhaseVisual VisualFor(LiveBoard board) => board switch
    {
        { Phase: BoardPhase.OurTurn, HandComplete: true } => new(
            FontAwesomeIcon.Trophy, Styling.AccentAmber, "Tsumo", "complete hand — declaring the win", 1400.0, 1f),
        { Phase: BoardPhase.CallOffered, RonOffered: true } => new(
            FontAwesomeIcon.Trophy, Styling.AccentAmber, "Ron", "winning tile offered — taking it", 1400.0, 1f),
        { Phase: BoardPhase.OurTurn } => new(
            FontAwesomeIcon.Bolt, Styling.Accent, "Playing the turn", "discarding the highest-efficiency tile", 1800.0, 0.8f),
        { Phase: BoardPhase.CallOffered } => new(
            FontAwesomeIcon.Times, Styling.AccentBlue, "Passing the call", "keeping the hand concealed", 2200.0, 0.55f),
        { Phase: BoardPhase.BetweenHands } => new(
            FontAwesomeIcon.Forward, Styling.AccentBlue, "Advancing", "clearing the result screen", 2200.0, 0.55f),
        _ => new(
            FontAwesomeIcon.Eye, Styling.AccentBlueSoft, "Watching the table", "waiting for our turn", 3400.0, 0.35f),
    };

    private static void DrawAutopilot(PhaseVisual visual)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var radius = RingRadius * scale;
        var thickness = RingThickness * scale;

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var blockHeight = radius * 2f + 12f * scale;
        var center = new Vector2(origin.X + avail * 0.5f, origin.Y + blockHeight * 0.5f);

        var pulse = Styling.Pulse(Styling.PulseBreath);
        ProgressRing.Glow(center, radius, visual.Accent, (0.35f + 0.45f * pulse) * (0.4f + visual.Energy));

        drawList.AddCircleFilled(center, radius - thickness * 0.5f,
            ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, visual.Accent, 0.10f)));
        ProgressRing.Track(center, radius, thickness, Styling.WithAlpha(visual.Accent, 0.16f));
        ProgressRing.Sweep(center, radius, thickness, visual.Accent, visual.SweepPeriod,
            MathF.PI * (0.35f + 0.35f * visual.Energy), 0.9f);
        ProgressRing.CenterIcon(center, visual.Icon,
            Styling.WithAlpha(visual.Accent, 0.75f + 0.25f * pulse), radius * 0.58f);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, blockHeight));

        Styling.VSpace(8);
        Styling.TextCentered(visual.Title, Styling.TextStrong, 1.18f);
        Styling.VSpace(2);
        Styling.TextCentered(visual.Sub, Styling.TextDim, 0.9f);
    }

    // ---- Session scoreboard ------------------------------------------------

    internal static void DrawSessionStats(SessionStats stats)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var size = new Vector2((avail - gap * 2f) / 3f, StatTileHeight * scale);

        StatTile.Draw(FontAwesomeIcon.Trophy, stats.Wins.ToString(), "wins", Styling.AccentAmber, size);
        ImGui.SameLine(0, gap);
        StatTile.Draw(FontAwesomeIcon.ArrowDown, stats.Discards.ToString(), "discards", Styling.Accent, size);
        ImGui.SameLine(0, gap);
        StatTile.Draw(FontAwesomeIcon.Clock, FormatElapsed(stats.Elapsed), "runtime", Styling.AccentBlue, size);
    }

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";

    // ---- Session log -------------------------------------------------------

    private static void DrawSessionLog(SessionStats stats)
    {
        var scale = ImGuiHelpers.GlobalScale;
        Styling.SectionLabel("Session log");
        Styling.VSpace(3);

        var rows = Math.Min(stats.Recent.Count, LogRows);
        for (var i = 0; i < rows; i++)
        {
            DrawLogRow(stats.Recent[i], scale, faded: i > 0);
        }
    }

    private static void DrawLogRow(AutoAction action, float scale, bool faded)
    {
        var (icon, accent) = action.Kind switch
        {
            AutoActionKind.Tsumo or AutoActionKind.Ron => (FontAwesomeIcon.Trophy, Styling.AccentAmber),
            AutoActionKind.Discard => (FontAwesomeIcon.ArrowDown, Styling.AccentSoft),
            AutoActionKind.Advance => (FontAwesomeIcon.Forward, Styling.AccentBlue),
            AutoActionKind.Riichi => (FontAwesomeIcon.Bolt, Styling.AccentAmber),
            _ => (FontAwesomeIcon.Times, Styling.TextDim),
        };

        var alpha = faded ? 0.62f : 1f;
        var iconString = icon.ToIconString();

        ImGui.SetWindowFontScale(0.85f);
        Vector2 iconSize;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            iconSize = ImGui.CalcTextSize(iconString);

        // Fixed icon column so the labels align regardless of glyph width.
        var column = 16f * scale;
        var indent = ImGui.GetCursorPosX();
        ImGui.SetCursorPosX(indent + (column - iconSize.X) * 0.5f);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.WithAlpha(accent, alpha)))
            ImGui.TextUnformatted(iconString);

        ImGui.SameLine(indent + column, 6f * scale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.WithAlpha(Styling.TextSecondary, alpha)))
            ImGui.TextUnformatted(action.Label);

        var age = AgeText(action.Tick);
        var ageWidth = ImGui.CalcTextSize(age).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - ageWidth);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.WithAlpha(Styling.TextMuted, alpha)))
            ImGui.TextUnformatted(age);
        ImGui.SetWindowFontScale(1f);
    }

    private static string AgeText(long tick)
    {
        var seconds = (Environment.TickCount64 - tick) / 1000;
        return seconds switch
        {
            < 5 => "now",
            < 60 => $"{seconds}s",
            < 3600 => $"{seconds / 60}m",
            _ => $"{seconds / 3600}h",
        };
    }
}
