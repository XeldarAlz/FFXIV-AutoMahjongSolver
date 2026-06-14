using AutoMahjongSolver.Windows.Components;

namespace AutoMahjongSolver.Windows.Sections.Config;

internal static class AutoPlaySettings
{
    public static void Draw(Configuration cfg)
    {
        DrawTimingGroup(cfg);
        SettingsGroup.Footnote("Pick Suggestions or Auto-play from the main window. Suggestions only shows the best move and sends no inputs; Auto-play discards, declares tsumo/ron and passes calls for you (CASUAL tables only). Riichi is never declared for you. The timing below applies to Auto-play.");
    }

    private static void DrawTimingGroup(Configuration cfg)
    {
        using var group = SettingsGroup.Begin("Humanized timing");

        SettingsRow.Draw("Discard think time",
            "Waits a random time in this range before discarding, so the bot never reacts the instant it's your turn.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawSecondsRange(cfg, "##turn_min", "##turn_max",
                () => cfg.TurnDelayMinMs, value => cfg.TurnDelayMinMs = value,
                () => cfg.TurnDelayMaxMs, value => cfg.TurnDelayMaxMs = value, 5f));

        SettingsRow.Draw("Call reaction",
            "How long to wait before passing or accepting an offered call (pon, chi, kan, ron).",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawSecondsRange(cfg, "##call_min", "##call_max",
                () => cfg.CallDelayMinMs, value => cfg.CallDelayMinMs = value,
                () => cfg.CallDelayMaxMs, value => cfg.CallDelayMaxMs = value, 5f));

        SettingsRow.Draw("Results pause",
            "How long to let the results / new-hand screen settle before advancing it.",
            SettingsControls.RangeInlineWidth(),
            () => SettingsControls.DrawSecondsRange(cfg, "##result_min", "##result_max",
                () => cfg.ResultDelayMinMs, value => cfg.ResultDelayMinMs = value,
                () => cfg.ResultDelayMaxMs, value => cfg.ResultDelayMaxMs = value, 6f));
    }
}
