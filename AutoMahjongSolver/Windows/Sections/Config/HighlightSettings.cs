using AutoMahjongSolver.Windows.Components;

namespace AutoMahjongSolver.Windows.Sections.Config;

internal static class HighlightSettings
{
    public static void Draw(Configuration cfg)
    {
        DrawGroup(cfg);
        SettingsGroup.Footnote("The marker is drawn in Suggest mode only and never sends an input — it just points out which tile to discard. Auto-play cuts the tile for you, so it needs no marker.");
    }

    private static void DrawGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("In-game highlight");

        SettingsRow.Draw("Show highlight",
            "In Suggest mode, draw a marker over the recommended discard tile on the live mahjong board, so you can read the move without watching this window.",
            SettingsControls.ToggleWidth,
            () => SettingsControls.DrawToggle(cfg, () => cfg.HighlightSuggestedTile, value => cfg.HighlightSuggestedTile = value),
            SettingsRow.ToggleHeight);

        if (!cfg.HighlightSuggestedTile)
            return;

        SettingsRow.Draw("Style",
            "The shape the in-game marker takes: framing brackets, a pointing arrow, or a soft glow.",
            SettingsControls.RowComboWidth,
            () => SettingsControls.Choices.DrawCombo("##hlstyle", OverlayTheme.StyleChoices, (int)cfg.HighlightStyle, value =>
            {
                cfg.HighlightStyle = (HighlightStyle)value;
                cfg.SaveDebounced();
            }));

        SettingsRow.Draw("Colour",
            "The colour palette for the in-game marker. Pick whatever reads best against your tile design.",
            ColorPalettePicker.Width(OverlayTheme.Swatches.Length),
            () =>
            {
                if (ColorPalettePicker.Draw("##hlcolour", OverlayTheme.Swatches, (int)cfg.HighlightColor, out int picked))
                {
                    cfg.HighlightColor = (HighlightColor)picked;
                    cfg.SaveDebounced();
                }
            },
            ColorPalettePicker.Height);
    }
}
