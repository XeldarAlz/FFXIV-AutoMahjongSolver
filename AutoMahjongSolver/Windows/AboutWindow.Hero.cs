using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.IO;
using System.Numerics;

namespace AutoMahjongSolver.Windows;

public sealed partial class AboutWindow
{
    private static readonly string VersionText =
        typeof(AboutWindow).Assembly.GetName().Version?.ToString() ?? "?";

    private static readonly string IconImagePath =
        Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "Images", IconFile);

    private void DrawHero()
    {
        var s = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();

        Styling.VSpace(34);

        const float iconSize = 150f;
        var start = ImGui.GetCursorScreenPos();
        var availX = ImGui.GetContentRegionAvail().X;
        var bob = Wave(3000) * 3f * s;
        var center = new Vector2(start.X + availX * 0.5f, start.Y + iconSize * 0.5f * s + bob);

        ProgressRing.Glow(center, iconSize * 0.58f * s, Styling.Accent, 0.55f + 0.5f * Styling.Pulse(Styling.PulseBreath));

        var half = iconSize * 0.5f * s;
        var iconMin = new Vector2(center.X - half, center.Y - half);
        var iconMax = new Vector2(center.X + half, center.Y + half);
        var rounding = iconSize * 0.16f * s;

        if (File.Exists(IconImagePath))
        {
            var texture = Plugin.TextureProvider.GetFromFile(IconImagePath).GetWrapOrEmpty();
            if (texture != null)
            {
                var alpha = 0.94f + 0.06f * Styling.Pulse(2200.0);
                drawList.AddImageRounded(texture.Handle, iconMin, iconMax, Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(Styling.WithAlpha(Styling.White, alpha)), rounding, ImDrawFlags.RoundCornersAll);
            }
        }

        drawList.AddRect(iconMin, iconMax, ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentSoft, 0.55f)),
            rounding, ImDrawFlags.RoundCornersAll, 1.5f * s);

        IconEasterEgg(iconMin, iconMax, s);

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(availX, iconSize * s + 22f * s));

        Styling.VSpace(10);
        ShimmerCentered(Name, Styling.TextStrong, Styling.AccentSoft, 1.85f, Styling.PulseOrbit, 0.42f);
        Styling.VSpace(9);

        CenteredPill($"v {VersionText}", Styling.TextSecondary,
            Styling.WithAlpha(Styling.Accent, 0.45f), Styling.CardBgSoft);
    }

    private static void ShimmerCentered(string text, Vector4 baseColor, Vector4 shimmerColor,
        float fontScale, double periodMs, float bandFrac)
    {
        ImGui.SetWindowFontScale(fontScale);
        var size = ImGui.CalcTextSize(text);
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > size.X)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) * 0.5f);

        var startScreen = ImGui.GetCursorScreenPos();

        var bloom = Styling.WithAlpha(Styling.Accent, 0.22f);
        for (var offsetIndex = 0; offsetIndex < BloomOffsets.Length; offsetIndex++)
        {
            ImGui.SetCursorScreenPos(startScreen + BloomOffsets[offsetIndex] * ImGuiHelpers.GlobalScale);
            using (ImRaii.PushColor(ImGuiCol.Text, bloom))
                ImGui.TextUnformatted(text);
        }

        ImGui.SetCursorScreenPos(startScreen);
        using (ImRaii.PushColor(ImGuiCol.Text, baseColor))
            ImGui.TextUnformatted(text);

        var dl = ImGui.GetWindowDrawList();
        var bandW = size.X * bandFrac;
        var phase = Styling.Phase(periodMs);
        var bandCenter = startScreen.X - bandW + phase * (size.X + bandW * 2f);

        dl.PushClipRect(
            new Vector2(bandCenter - bandW * 0.5f, startScreen.Y),
            new Vector2(bandCenter + bandW * 0.5f, startScreen.Y + size.Y),
            true);
        ImGui.SetCursorScreenPos(startScreen);
        using (ImRaii.PushColor(ImGuiCol.Text, shimmerColor))
            ImGui.TextUnformatted(text);
        dl.PopClipRect();

        ImGui.SetWindowFontScale(1f);
    }

    private static void CenteredPill(string text, Vector4 textColor, Vector4 borderColor, Vector4 bgColor)
    {
        var s = ImGuiHelpers.GlobalScale;
        var padX = 11f * s;
        var padY = 3f * s;
        var ts = ImGui.CalcTextSize(text);
        var w = ts.X + padX * 2f;
        var h = ts.Y + padY * 2f;

        Styling.CenterNextItem(w);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + new Vector2(w, h);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(bgColor), h * 0.5f);
        dl.AddRect(origin, end, ImGui.GetColorU32(borderColor), h * 0.5f, ImDrawFlags.None, 1f);

        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + padY));
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
            ImGui.TextUnformatted(text);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(w, h));
    }
}
