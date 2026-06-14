using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows;

/// <summary>
/// The plugin's violet theme. <see cref="Accent"/> is the brand colour; the remaining accents cover
/// success, warning, danger and information. Tile and suit colours below drive the rendered tile faces
/// (see <c>TileGlyph</c>): ivory faces with traditional suit inks, and a violet back to match the icon.
/// </summary>
internal static class Styling
{
    // Brand + semantic accents (original palette).
    public static readonly Vector4 Accent      = new(0.62f, 0.42f, 0.96f, 1.00f); // violet
    public static readonly Vector4 AccentSoft  = new(0.78f, 0.60f, 1.00f, 1.00f);
    public static readonly Vector4 AccentPink       = new(0.95f, 0.45f, 0.78f, 1.00f);
    public static readonly Vector4 AccentMint       = new(0.46f, 0.86f, 0.66f, 1.00f);
    public static readonly Vector4 AccentMintSoft   = new(0.66f, 0.96f, 0.80f, 1.00f);
    public static readonly Vector4 AccentAmber      = new(0.92f, 0.74f, 0.34f, 1.00f);
    public static readonly Vector4 AccentAmberSoft  = new(1.00f, 0.86f, 0.52f, 1.00f);
    public static readonly Vector4 AccentRose       = new(0.93f, 0.42f, 0.50f, 1.00f);
    public static readonly Vector4 AccentBlue       = new(0.40f, 0.68f, 0.98f, 1.00f);
    public static readonly Vector4 AccentBlueSoft   = new(0.62f, 0.82f, 1.00f, 1.00f);

    public static readonly Vector4 CardBg      = new(0.075f, 0.090f, 0.105f, 0.85f);
    public static readonly Vector4 CardBgSoft  = new(0.090f, 0.105f, 0.120f, 0.55f);
    public static readonly Vector4 CardBgHover = new(0.105f, 0.125f, 0.145f, 0.95f);
    public static readonly Vector4 SliderBg    = new(0.20f, 0.22f, 0.26f, 1.00f);
    public static readonly Vector4 BorderDim   = new(0.22f, 0.25f, 0.30f, 1.00f);

    public static readonly Vector4 TextStrong    = new(0.96f, 0.96f, 0.97f, 1.00f);
    public static readonly Vector4 TextSecondary = new(0.82f, 0.84f, 0.88f, 1.00f);
    public static readonly Vector4 TextDim       = new(0.67f, 0.70f, 0.75f, 1.00f);
    public static readonly Vector4 TextMuted     = new(0.50f, 0.53f, 0.58f, 1.00f);

    public static readonly Vector4 Hairline = new(1f, 1f, 1f, 0.055f);
    public static readonly Vector4 White    = new(1f, 1f, 1f, 1f);

    // Tile faces and suit inks (ivory tile to match the icon; traditional suit colours for legibility).
    public static readonly Vector4 TileFace    = new(0.93f, 0.91f, 0.86f, 1.00f); // ivory
    public static readonly Vector4 TileFaceTop = new(0.98f, 0.97f, 0.93f, 1.00f); // bevel highlight
    public static readonly Vector4 TileEdge    = new(0.58f, 0.54f, 0.50f, 1.00f);
    public static readonly Vector4 TileShadow  = new(0f, 0f, 0f, 0.40f);
    public static readonly Vector4 TileBack    = new(0.32f, 0.24f, 0.46f, 1.00f); // violet back

    public static readonly Vector4 SuitMan   = new(0.72f, 0.18f, 0.20f, 1.00f); // maroon
    public static readonly Vector4 SuitPin   = new(0.16f, 0.34f, 0.66f, 1.00f); // indigo
    public static readonly Vector4 SuitSou   = new(0.18f, 0.50f, 0.26f, 1.00f); // bamboo
    public static readonly Vector4 SuitHonor = new(0.22f, 0.24f, 0.28f, 1.00f); // ink
    public static readonly Vector4 DragonGreen = new(0.13f, 0.52f, 0.30f, 1.00f);
    public static readonly Vector4 DragonRed   = new(0.78f, 0.18f, 0.18f, 1.00f);
    public static readonly Vector4 AkaRed       = new(0.86f, 0.20f, 0.18f, 1.00f);

    public const float CardRounding = 7f;
    public const float FrameRounding = 5f;
    public const float WindowRounding = 7f;
    public const float TileRounding = 4f;

    public const double PulseMedium = 800.0;

    public const double PulseBreath = 2600.0;
    public const double PulseCalm = 1900.0;
    public const double PulseOrbit = 3400.0;

    public static float Pulse(double periodMs = PulseMedium)
    {
        var t = (Environment.TickCount % periodMs) / periodMs;
        return (float)((Math.Sin(t * Math.PI * 2.0) + 1.0) * 0.5);
    }

    public static Vector4 PulseColor(Vector4 a, Vector4 b, double periodMs = PulseMedium)
        => Vector4.Lerp(a, b, Pulse(periodMs));

    public static float Phase(double periodMs)
        => (float)((Environment.TickCount % periodMs) / periodMs);

    public static float EaseOutCubic(float t)
    {
        var inv = 1f - Math.Clamp(t, 0f, 1f);
        return 1f - inv * inv * inv;
    }

    public static Vector4 WithAlpha(Vector4 c, float a) => c with { W = a };

    public static Vector4 Lighten(Vector4 c, float t) => Vector4.Lerp(c, White, t) with { W = c.W };

    public static void TextCentered(string text, Vector4 color, float fontScale = 1f)
    {
        if (fontScale != 1f) ImGui.SetWindowFontScale(fontScale);
        var w = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > w) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - w) * 0.5f);
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(text);
        if (fontScale != 1f) ImGui.SetWindowFontScale(1f);
    }

    public static void VSpace(float pixels)
        => ImGui.Dummy(new Vector2(0, pixels * ImGuiHelpers.GlobalScale));

    public static void CenterNextItem(float width)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (avail - width) * 0.5f));
    }

    public static IDisposable PushCardStyle()
    {
        var p = ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, CardRounding * ImGuiHelpers.GlobalScale);
        p.Push(ImGuiStyleVar.ChildBorderSize, 1f);
        p.Push(ImGuiStyleVar.WindowPadding, new Vector2(11, 9) * ImGuiHelpers.GlobalScale);
        p.Push(ImGuiStyleVar.FrameRounding, FrameRounding);
        return p;
    }

    public static IDisposable PushWindowStyle()
    {
        var p = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, FrameRounding);
        p.Push(ImGuiStyleVar.WindowRounding, WindowRounding);
        p.Push(ImGuiStyleVar.ChildRounding, CardRounding);
        p.Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 7) * ImGuiHelpers.GlobalScale);
        return p;
    }

    public static void SectionLabel(string label)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted(label.ToUpperInvariant());
    }

    public static void HairlineRule(float spacingBefore = 0f, float spacingAfter = 0f)
    {
        if (spacingBefore > 0f)
        {
            VSpace(spacingBefore);
        }

        var drawList = ImGui.GetWindowDrawList();
        var lineStart = ImGui.GetCursorScreenPos();
        var lineWidth = ImGui.GetContentRegionAvail().X;
        drawList.AddLine(lineStart, lineStart + new Vector2(lineWidth, 0), ImGui.GetColorU32(Hairline), 1f);
        ImGui.Dummy(new Vector2(lineWidth, 1f));

        if (spacingAfter > 0f)
        {
            VSpace(spacingAfter);
        }
    }
}
