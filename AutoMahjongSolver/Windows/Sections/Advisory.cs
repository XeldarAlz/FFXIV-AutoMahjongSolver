using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace AutoMahjongSolver.Windows.Sections;

/// <summary>
/// Compatibility feedback. Escalates from a subtle tip to a specific warning when a problem is actually
/// observed: a non-English client (panel text matching is English-only) or hand tiles that are on screen
/// but decode to no known texture (an incompatible tile design, or a game patch).
/// </summary>
internal static class Advisory
{
    public static void Draw(LiveBoard board)
    {
        bool nonEnglish = Plugin.ClientState.ClientLanguage != ClientLanguage.English;
        bool tilesBroken = board.TilesUnreadable;

        if (tilesBroken)
            Row(FontAwesomeIcon.ExclamationTriangle, Styling.AccentRose, Styling.AccentRose,
                "Tile textures not recognized — the solver can't read your hand. If you changed the tile design, "
                + "switch back to the default. (A game update can also cause this; run /amjs tree to inspect.)");

        if (nonEnglish)
            Row(FontAwesomeIcon.ExclamationTriangle, Styling.AccentAmber, Styling.TextSecondary,
                $"Non-English client ({Plugin.ClientState.ClientLanguage}) — seat wind and riichi are only verified "
                + "in English and may be misread. Run /amjs state to help add your language.");

        if (!tilesBroken && !nonEnglish)
            Row(FontAwesomeIcon.InfoCircle, Styling.TextDim, Styling.TextDim,
                "For best results, use the default Doman Mahjong tile design and an English client.");

        Styling.HairlineRule(2f, 2f);
    }

    private static void Row(FontAwesomeIcon icon, Vector4 iconColor, Vector4 textColor, string message)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, iconColor))
            ImGui.TextUnformatted(icon.ToIconString());

        ImGui.SameLine(0, 6 * ImGuiHelpers.GlobalScale);
        using (ImRaii.PushColor(ImGuiCol.Text, textColor))
            ImGui.TextWrapped(message);
    }
}
