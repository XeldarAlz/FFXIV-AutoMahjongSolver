using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Mahjong.Plugin.Dalamud.UI;

/// <summary>Settings, grouped into Auto-play / Appearance / Developer cards.</summary>
public sealed class SettingsWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    /// <summary>Which color the preview row renders — purely UI state, not persisted.</summary>
    private bool previewShowsTsumogiri;

    public SettingsWindow(Plugin plugin)
        : base("Doman Mahjong Solver — Settings###domanmahjong-settings")
    {
        ArgumentNullException.ThrowIfNull(plugin);
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(480, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(800, 1200),
        };
    }

    public void Dispose() { }

    private static string StyleLabel(HighlightStyle s) => s switch
    {
        HighlightStyle.Arrow => "Big arrow + label",
        _ => "Neon glow + corner brackets",
    };

    private void DrawHighlightPreview(Configuration cfg)
    {
        // Radio above the preview to flip which color is being shown.
        bool showDiscard = !previewShowsTsumogiri;
        if (ImGui.RadioButton("Preview: Discard", showDiscard))
            previewShowsTsumogiri = false;
        ImGui.SameLine(0, 14);
        if (ImGui.RadioButton("Tsumogiri", !showDiscard))
            previewShowsTsumogiri = true;

        // Reserve a canvas wide enough for 5 fake tiles + headroom for the arrow/label.
        const int tileCount = 5;
        const float tileW = 34f;
        const float tileH = 50f;
        const float tileGap = 6f;
        const float topPad = 56f;   // room above for the arrow/pill
        const float botPad = 12f;

        float canvasW = ImGui.GetContentRegionAvail().X;
        float canvasH = topPad + tileH + botPad;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(canvasW, canvasH));

        var dl = ImGui.GetWindowDrawList();

        // Backing panel so the preview reads as its own surface.
        dl.AddRectFilled(origin, origin + new Vector2(canvasW, canvasH), Theme.Pack(Theme.SurfaceAlt), 6f);
        dl.AddRect(origin, origin + new Vector2(canvasW, canvasH), Theme.Pack(Theme.Border), 6f, ImDrawFlags.None, 1f);

        // Lay out 5 fake tiles centered horizontally.
        float rowW = tileCount * tileW + (tileCount - 1) * tileGap;
        float startX = origin.X + (canvasW - rowW) * 0.5f;
        float tileY = origin.Y + topPad;
        var rects = new List<(Vector2 Pos, Vector2 Size)>(tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            var pos = new Vector2(startX + i * (tileW + tileGap), tileY);
            var size = new Vector2(tileW, tileH);
            rects.Add((pos, size));
            // Shadow + tile face + border, same recipe as Theme.DrawTile but without an item dummy.
            dl.AddRectFilled(pos + new Vector2(1, 2), pos + size + new Vector2(1, 2), Theme.Pack(Theme.TileShadow), 4f);
            dl.AddRectFilled(pos, pos + size, Theme.Pack(Theme.TileFace), 4f);
            dl.AddRect(pos, pos + size, Theme.Pack(Theme.TileBorder), 4f, ImDrawFlags.None, 1.5f);
        }

        int pickSlot = tileCount / 2;
        bool isDrawnTile = previewShowsTsumogiri;
        var color = (isDrawnTile ? cfg.HighlightColorTsumogiri : cfg.HighlightColorDiscard).ToVector3();
        float intensity = Math.Clamp(cfg.HighlightIntensity, 0.4f, 1.6f);

        // Clip to the preview panel so the arrow/glow can't escape it.
        dl.PushClipRect(origin, origin + new Vector2(canvasW, canvasH), true);
        switch (cfg.HighlightStyle)
        {
            case HighlightStyle.Arrow:
                HandOverlay.DrawHighlightArrow(dl, rects[pickSlot], color, intensity, isDrawnTile);
                break;
            default:
                HandOverlay.DrawHighlightNeonGlow(dl, rects[pickSlot], color, intensity);
                break;
        }
        dl.PopClipRect();
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        using var _s = Theme.PushWindowStyle();

        using (Theme.BeginCard("settings-play"))
        {
            Theme.SectionHeader("Auto-play");

            int delay = cfg.HumanizedDelayMs;
            ImGui.SetNextItemWidth(300);
            if (ImGui.SliderInt("Click speed", ref delay, 400, 3000, "%d ms"))
                plugin.ConfigService.Update(c => c with { HumanizedDelayMs = delay });
            Theme.Subtle("Average delay before each auto-play click.");
        }

        ImGui.Dummy(new Vector2(0, 4));

        using (Theme.BeginCard("settings-appearance"))
        {
            Theme.SectionHeader("Appearance");

            bool highlight = cfg.ShowInGameHighlight;
            if (ImGui.Checkbox("Highlight suggested tile in the mahjong window", ref highlight))
                plugin.ConfigService.Update(c => c with { ShowInGameHighlight = highlight });
            Theme.Subtle("A pulsing outline on the discard to make. Shown in Hints mode.");

            ImGui.Dummy(new Vector2(0, 4));

            if (!highlight)
                ImGui.BeginDisabled();

            var style = cfg.HighlightStyle;
            ImGui.SetNextItemWidth(300);
            if (ImGui.BeginCombo("Highlight style", StyleLabel(style)))
            {
                foreach (var opt in new[] { HighlightStyle.NeonGlow, HighlightStyle.Arrow })
                {
                    bool selected = opt == style;
                    if (ImGui.Selectable(StyleLabel(opt), selected) && opt != style)
                        plugin.ConfigService.Update(c => c with { HighlightStyle = opt });
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            Theme.Subtle(style switch
            {
                HighlightStyle.Arrow => "Large arrow with a DISCARD / TSUMOGIRI label. Keeps the tile art uncovered.",
                _ => "Neon glow halo, L-shaped corner brackets, and a bouncing arrow above the tile.",
            });

            ImGui.Dummy(new Vector2(0, 6));

            DrawHighlightPreview(cfg);

            ImGui.Dummy(new Vector2(0, 6));

            // Discard color.
            var discardVec = cfg.HighlightColorDiscard.ToVector3();
            if (ImGui.ColorEdit3("Discard color", ref discardVec,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
                plugin.ConfigService.Update(c => c with { HighlightColorDiscard = RgbColor.From(discardVec) });

            // Tsumogiri color.
            var tsuVec = cfg.HighlightColorTsumogiri.ToVector3();
            if (ImGui.ColorEdit3("Tsumogiri color", ref tsuVec,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
                plugin.ConfigService.Update(c => c with { HighlightColorTsumogiri = RgbColor.From(tsuVec) });

            // Intensity slider.
            float intensity = cfg.HighlightIntensity;
            ImGui.SetNextItemWidth(300);
            if (ImGui.SliderFloat("Intensity", ref intensity, 0.4f, 1.6f, "%.2fx"))
                plugin.ConfigService.Update(c => c with { HighlightIntensity = intensity });

            // Reset button.
            if (ImGui.SmallButton("Reset overlay colors & intensity"))
                plugin.ConfigService.Update(c => c with
                {
                    HighlightColorDiscard = RgbColor.Defaults.Discard,
                    HighlightColorTsumogiri = RgbColor.Defaults.Tsumogiri,
                    HighlightIntensity = 1.0f,
                });

            if (!highlight)
                ImGui.EndDisabled();

            ImGui.Dummy(new Vector2(0, 4));

            bool details = cfg.ShowSuggestionDetails;
            if (ImGui.Checkbox("Show analysis details under best move", ref details))
                plugin.ConfigService.Update(c => c with { ShowSuggestionDetails = details });
            Theme.Subtle("Adds a shanten / ukeire breakdown of the top discards in the main window.");
        }

        ImGui.Dummy(new Vector2(0, 4));

        using (Theme.BeginCard("settings-dev"))
        {
            Theme.SectionHeader("Developer");

            bool dev = cfg.DevMode;
            if (ImGui.Checkbox("Enable developer tools", ref dev))
            {
                plugin.ConfigService.Update(c => c with { DevMode = dev });
                if (dev)
                    plugin.DebugOverlay.IsOpen = true;
            }
            Theme.Subtle("Adds a debug button to the main window toolbar.");
        }
    }
}
