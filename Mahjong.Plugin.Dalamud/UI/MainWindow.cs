using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Policy;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Plugin.Dalamud.UI;

/// <summary>End-user window: toolbar, status, mode, live game. Settings and debug live in their own windows.</summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Doman Mahjong Solver###domanmahjong-main")
    {
        this.plugin = plugin;
        Size = new Vector2(520, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 420),
            MaximumSize = new Vector2(900, 2000),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        var cfg = plugin.Configuration;

        using var _s = Theme.PushWindowStyle();

        if (!cfg.TosAccepted)
        {
            DrawTosGate(cfg);
            return;
        }

        DrawTopToolbar(cfg);
        DrawModeCard(cfg);
        ImGui.Dummy(new Vector2(0, 4));
        DrawLiveCard();

        DrawAutoPlayConfirmModal(cfg);
    }

    private void DrawTopToolbar(Configuration cfg)
    {
        var bugLabel = FontAwesomeIcon.Bug.ToIconString();
        var infoLabel = FontAwesomeIcon.InfoCircle.ToIconString();
        var gearLabel = FontAwesomeIcon.Cog.ToIconString();

        bool bugClicked = false, infoClicked, gearClicked;
        // Tooltips render outside the icon-font scope — they would use icon glyphs otherwise.
        int hovered = -1;
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var framePadX = ImGui.GetStyle().FramePadding.X;
            var spacingX = ImGui.GetStyle().ItemSpacing.X;
            var btnW = ImGui.CalcTextSize(gearLabel).X + framePadX * 2;

            int slots = cfg.DevMode ? 3 : 2;
            float totalW = btnW * slots + spacingX * (slots - 1);
            float avail = ImGui.GetContentRegionAvail().X;
            if (avail > totalW)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - totalW);

            if (cfg.DevMode)
            {
                bugClicked = ImGui.Button(bugLabel + "##debug");
                if (ImGui.IsItemHovered()) hovered = 0;
                ImGui.SameLine();
            }
            infoClicked = ImGui.Button(infoLabel + "##about");
            if (ImGui.IsItemHovered()) hovered = 1;
            ImGui.SameLine();
            gearClicked = ImGui.Button(gearLabel + "##settings");
            if (ImGui.IsItemHovered()) hovered = 2;
        }

        switch (hovered)
        {
            case 0: ImGui.SetTooltip("Developer console"); break;
            case 1: ImGui.SetTooltip("About"); break;
            case 2: ImGui.SetTooltip("Settings"); break;
        }

        if (bugClicked) plugin.ToggleDebugOverlay();
        if (infoClicked) plugin.ToggleAboutWindow();
        if (gearClicked) plugin.ToggleSettingsWindow();
    }

    private void DrawTosGate(Configuration cfg)
    {
        using (Theme.BeginCard("tos"))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
            ImGui.TextUnformatted("Welcome to Doman Mahjong Solver");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 6));

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
            ImGui.TextWrapped(
                "This plugin can play Doman Mahjong for you by reading the game and clicking " +
                "on your behalf. Before turning that on, please read:");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 4));

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
            ImGui.BulletText("Third-party automation is against the FFXIV Terms of Service.");
            ImGui.BulletText("Use at your own risk — your account may be sanctioned.");
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
            ImGui.BulletText("\"Hints\" mode only shows advice — never clicks for you.");
            ImGui.TextWrapped(
                "  • This build uploads anonymous gameplay logs, error reports, and " +
                "memory diagnostics from the Mahjong addon to support cross-client " +
                "reverse-engineering. No character names, Content IDs, or other PII " +
                "are included — uploads are keyed only by a random per-install ID.");
            ImGui.PopStyleColor();

            ImGui.Dummy(new Vector2(0, 10));

            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(Theme.Accent.X * 1.15f, Theme.Accent.Y * 1.15f, Theme.Accent.Z * 1.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(Theme.Accent.X * 0.85f, Theme.Accent.Y * 0.85f, Theme.Accent.Z * 0.85f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.05f, 0.10f, 0.08f, 1f));
            float btnW = ImGui.GetContentRegionAvail().X;
            if (ImGui.Button("I understand — continue", new Vector2(btnW, 36)))
                plugin.ConfigService.Update(c => c with { TosAccepted = true });
            ImGui.PopStyleColor(4);
        }
    }

    private void DrawModeCard(Configuration cfg)
    {
        using (Theme.BeginCard("mode"))
        {
            // Header row: "Mode" label + right-aligned in-match / idle badge.
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
            ImGui.TextUnformatted("Mode");
            ImGui.PopStyleColor();

            bool addonOk = plugin.AddonReader.Poll().Present;
            string badgeText = addonOk ? "in match" : "idle";
            var badgeTint = addonOk ? Theme.Accent : Theme.Muted;
            float badgeW = ImGui.CalcTextSize(badgeText).X + 28;
            Theme.RightAlign(badgeW, Theme.CardPadX);
            Theme.Pill(badgeText, badgeTint, filled: false);

            var dl = ImGui.GetWindowDrawList();
            var p = ImGui.GetCursorScreenPos();
            float rw = ImGui.GetContentRegionAvail().X;
            dl.AddLine(p + new Vector2(0, 2), new Vector2(p.X + rw, p.Y + 2), Theme.Pack(Theme.Divider), 1f);
            ImGui.Dummy(new Vector2(0, 6));

            int current = !cfg.AutomationArmed ? 0 : (cfg.SuggestionOnly ? 1 : 2);
            float avail = ImGui.GetContentRegionAvail().X;
            float gap = 6f;
            float w = (avail - gap * 2) / 3f;
            var size = new Vector2(w, 50);

            if (ModePill("Off", "Do nothing", Theme.Muted, current == 0, size))
                RequestMode(0, cfg);
            ImGui.SameLine(0, gap);
            if (ModePill("Hints", "Highlight best move", Theme.Warn, current == 1, size))
                RequestMode(1, cfg);
            ImGui.SameLine(0, gap);
            if (ModePill("Auto-play", "Click for you", Theme.Accent, current == 2, size))
                RequestMode(2, cfg);
        }
    }

    private static bool ModePill(string title, string sub, Vector4 tint, bool selected, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;

        bool clicked = ImGui.InvisibleButton($"##mode-{title}", size);
        bool hovered = ImGui.IsItemHovered();

        Vector4 bg = selected ? Theme.Fade(tint, 0.30f)
                     : hovered ? Theme.Fade(tint, 0.15f)
                               : Theme.Fade(tint, 0.07f);
        Vector4 border = selected ? tint : Theme.Fade(tint, 0.40f);

        dl.AddRectFilled(min, max, Theme.Pack(bg), 6f);
        dl.AddRect(min, max, Theme.Pack(border), 6f, ImDrawFlags.None, selected ? 2f : 1f);

        var titleSize = ImGui.CalcTextSize(title);
        var subSize = ImGui.CalcTextSize(sub);
        Vector4 titleColor = selected ? new Vector4(1f, 1f, 1f, 1f) : tint;
        Vector4 subColor = selected ? new Vector4(1f, 1f, 1f, 0.75f) : Theme.Fade(tint, 0.65f);
        var titlePos = min + new Vector2((size.X - titleSize.X) * 0.5f, 8);
        var subPos = min + new Vector2((size.X - subSize.X) * 0.5f, size.Y - subSize.Y - 6);
        dl.AddText(titlePos, Theme.Pack(titleColor), title);
        dl.AddText(subPos, Theme.Pack(subColor), sub);

        return clicked;
    }

    private bool autoPlayConfirmPending;

    private void RequestMode(int mode, Configuration cfg)
    {
        if (mode == 2 && !cfg.AutoPlayConfirmed)
        {
            autoPlayConfirmPending = true;
            return;
        }
        ApplyMode(mode);
    }

    private void ApplyMode(int mode) =>
        plugin.ConfigService.Update(c => c with
        {
            AutomationArmed = mode > 0,
            SuggestionOnly = mode == 1,
        });

    private void DrawAutoPlayConfirmModal(Configuration cfg)
    {
        const string id = "Enable Auto-play?##autoplay-confirm";
        if (autoPlayConfirmPending)
        {
            ImGui.OpenPopup(id);
            autoPlayConfirmPending = false;
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
        ImGui.TextWrapped("Auto-play will click for you with humanized timing.");
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 4));
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
        ImGui.PushTextWrapPos(360);
        ImGui.TextUnformatted(
            "Switch Mode to Off or Hints any time to stop. Third-party automation is " +
            "against the FFXIV Terms of Service — use at your own risk.");
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 10));

        if (ImGui.Button("Enable Auto-play", new Vector2(160, 28)))
        {
            plugin.ConfigService.Update(c => c with { AutoPlayConfirmed = true });
            ApplyMode(2);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(100, 28)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void DrawLiveCard()
    {
        using (Theme.BeginCard("live"))
        {
            Theme.SectionHeader("Live game");

            var snap = plugin.AddonReader.TryBuildSnapshot();
            if (snap is null)
            {
                DrawEmptyLive();
                return;
            }

            // Cache policy + scored discards — hand-row highlight and suggestion panel both need them.
            ScoredDiscard[]? scored = null;
            ActionChoice? choice = null;
            string? scorerError = null;
            if (snap.Legal.Can(ActionFlags.Discard))
            {
                try
                {
                    scored = DiscardScorer.Score(snap);
                    choice = plugin.Policy.Choose(snap);
                }
                catch (Exception ex)
                {
                    scorerError = ex.Message;
                }
            }
            int highlightSlot = -1;
            if (choice?.DiscardTile is { } t)
                highlightSlot = InputDispatcher.FindSlotOfTile(t, snap.Hand);

            DrawSeatRow(snap);
            ImGui.Dummy(new Vector2(0, 10));
            DrawHandRow(snap, highlightSlot);
            ImGui.Dummy(new Vector2(0, 10));
            DrawSuggestion(snap, scored, choice, scorerError);
        }
    }

    private void DrawEmptyLive()
    {
        var cfg = plugin.Configuration;
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
        ImGui.TextWrapped("Waiting for a Doman Mahjong table.");
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0, 4));

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        ImGui.TextWrapped("When a match starts, the plugin will:");
        ImGui.PopStyleColor();
        ImGui.Dummy(new Vector2(0, 2));

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Warn);
        ImGui.BulletText("Hints — outline the best discard in the mahjong window.");
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
        ImGui.BulletText("Auto-play — click the best discard for you with humanized timing.");
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0, 4));
        string modeHint = !cfg.AutomationArmed
            ? "Mode is currently Off. Pick Hints or Auto-play above to arm the plugin."
            : cfg.SuggestionOnly
                ? "Hints mode is active — open a match and watch for the outline."
                : "Auto-play is active — open a match and the plugin will take over.";
        Theme.Subtle(modeHint);
    }

    private void DrawSeatRow(StateSnapshot snap)
    {
        string[] labels = { "You", "Right", "Across", "Left" };
        float avail = ImGui.GetContentRegionAvail().X;
        float gap = 6f;
        float pillW = (avail - gap * 3) / 4f;
        for (int i = 0; i < 4; i++)
        {
            DrawSeatPill(labels[i], snap.Scores[i], isYou: i == 0, new Vector2(pillW, 40));
            if (i < 3)
                ImGui.SameLine(0, gap);
        }
    }

    private static void DrawSeatPill(string label, int score, bool isYou, Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        Vector4 tint = isYou ? Theme.Accent : Theme.Muted;
        Vector4 bg = Theme.Fade(tint, isYou ? 0.18f : 0.08f);

        dl.AddRectFilled(min, max, Theme.Pack(bg), 6f);
        dl.AddRect(min, max, Theme.Pack(tint, isYou ? 0.85f : 0.45f), 6f, ImDrawFlags.None, 1f);

        var labelSize = ImGui.CalcTextSize(label);
        var labelPos = min + new Vector2((size.X - labelSize.X) * 0.5f, 5);
        dl.AddText(labelPos, Theme.Pack(tint, 0.8f), label);

        string scoreStr = score.ToString();
        var scoreSize = ImGui.CalcTextSize(scoreStr);
        var scorePos = min + new Vector2((size.X - scoreSize.X) * 0.5f, size.Y - scoreSize.Y - 5);
        Vector4 scoreColor = isYou ? Theme.Header : Theme.Body;
        dl.AddText(scorePos, Theme.Pack(scoreColor), scoreStr);

        ImGui.Dummy(size);
    }

    private static void DrawHandRow(StateSnapshot snap, int highlightSlot)
    {
        Theme.Caption($"Hand · {snap.Hand.Count} tiles");
        ImGui.Dummy(new Vector2(0, 3));
        Theme.DrawHand(snap.Hand, highlightSlot);
    }

    private void DrawSuggestion(
        StateSnapshot snap,
        ScoredDiscard[]? scored,
        ActionChoice? choice,
        string? scorerError)
    {
        var cfg = plugin.Configuration;

        Theme.Caption("Best move");
        ImGui.Dummy(new Vector2(0, 3));

        if (!snap.Legal.Can(ActionFlags.Discard))
        {
            Theme.Subtle($"Waiting for your turn ({snap.Hand.Count} tiles in hand).");
            return;
        }

        if (scorerError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Danger);
            ImGui.TextWrapped($"scorer error: {scorerError}");
            ImGui.PopStyleColor();
            return;
        }

        if (scored is null || choice is null)
            return;

        string verb = FriendlyActionVerb(choice.Kind);

        float startY = ImGui.GetCursorPosY();
        float bigH = Theme.BigTileH;
        float textH = ImGui.CalcTextSize("X").Y;
        float textY = startY + (bigH - textH) * 0.5f;

        ImGui.SetCursorPosY(textY);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
        ImGui.TextUnformatted(verb);
        ImGui.PopStyleColor();

        if (choice.DiscardTile is { } t)
        {
            ImGui.SameLine(0, 12);
            ImGui.SetCursorPosY(startY);
            Theme.DrawTile(t, new Vector2(Theme.BigTileW, Theme.BigTileH), Theme.Pulse(1.4f, 0.55f, 1.0f));

            ImGui.SameLine(0, 12);
            ImGui.SetCursorPosY(textY);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
            ImGui.TextUnformatted(FriendlyTileName(t));
            ImGui.PopStyleColor();
        }

        if (choice.DiscardTile is not null)
            ImGui.SetCursorPosY(startY + bigH + 4);

        string why = ExplainChoice(choice, scored);
        if (!string.IsNullOrEmpty(why))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
            ImGui.TextWrapped(why);
            ImGui.PopStyleColor();
        }

        if (cfg.ShowInGameHighlight)
        {
            Theme.Subtle("The tile is outlined in the mahjong window.");
        }

        if (cfg.ShowSuggestionDetails)
        {
            ImGui.Dummy(new Vector2(0, 6));
            Theme.Subtle(
                "shanten = turns away from ready.  ukeire = tiles that complete your wait — " +
                "counted as distinct kinds and total copies remaining in the live wall.");
            ImGui.Dummy(new Vector2(0, 4));
            int show = Math.Min(3, scored.Length);
            for (int i = 0; i < show; i++)
                DrawScoredPickRow(i, scored[i]);
        }

        if (plugin.AutoPlay.LastActionDescription != "(none)")
        {
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
            ImGui.TextWrapped($"Last action: {plugin.AutoPlay.LastActionDescription}");
            ImGui.PopStyleColor();
        }
    }

    private static void DrawScoredPickRow(int rank, ScoredDiscard s)
    {
        float rowStart = ImGui.GetCursorPosY();
        float tileH = Theme.SmallTileH;
        float textH = ImGui.CalcTextSize("X").Y;
        float textY = rowStart + (tileH - textH) * 0.5f;

        ImGui.SetCursorPosY(textY);
        Vector4 rankColor = rank == 0 ? Theme.Accent : Theme.Muted;
        ImGui.PushStyleColor(ImGuiCol.Text, rankColor);
        ImGui.TextUnformatted($"{rank + 1}.");
        ImGui.PopStyleColor();

        ImGui.SameLine(0, 8);
        ImGui.SetCursorPosY(rowStart);
        Theme.DrawTile(s.Discard, new Vector2(Theme.SmallTileW, Theme.SmallTileH));

        ImGui.SameLine(0, 10);
        ImGui.SetCursorPosY(textY);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Body);
        ImGui.TextUnformatted($"shanten {s.ShantenAfter}    ukeire {s.UkeireKinds} kinds · {s.UkeireWeighted} tiles");
        ImGui.PopStyleColor();

        ImGui.SetCursorPosY(rowStart + tileH + 3);
    }

    private static string FriendlyActionVerb(ActionKind kind) => kind switch
    {
        ActionKind.Discard => "Discard",
        ActionKind.Riichi => "Riichi on",
        ActionKind.Tsumo => "Win (tsumo)",
        ActionKind.Ron => "Win (ron)",
        ActionKind.Pon => "Pon",
        ActionKind.Chi => "Chi",
        ActionKind.AnKan => "Kan",
        ActionKind.MinKan => "Kan",
        ActionKind.ShouMinKan => "Kan",
        _ => kind.ToString(),
    };

    private static string FriendlyTileName(Tile tile) => Theme.TileFriendlyName(tile);

    private static string ExplainChoice(ActionChoice choice, ScoredDiscard[] scored)
    {
        switch (choice.Kind)
        {
            case ActionKind.Tsumo:
            case ActionKind.Ron:
                return "Winning hand — close it out.";
            case ActionKind.Riichi:
                return "One tile from winning — declare riichi.";
            case ActionKind.Pon:
            case ActionKind.Chi:
            case ActionKind.AnKan:
            case ActionKind.MinKan:
            case ActionKind.ShouMinKan:
                return "Calling completes a set faster than drawing for it.";
        }

        if (scored.Length == 0) return "";
        var top = scored[0];
        string kindNoun = top.UkeireKinds == 1 ? "kind" : "kinds";
        string tileNoun = top.UkeireWeighted == 1 ? "tile" : "tiles";

        if (top.ShantenAfter < 0)
            return "Discarding this still leaves a winning hand.";
        if (top.ShantenAfter == 0)
            return $"Keeps you ready — {top.UkeireKinds} {kindNoun} ({top.UkeireWeighted} {tileNoun} live) complete the hand.";
        if (top.ShantenAfter == 1)
            return $"One step from ready, with {top.UkeireKinds} useful {kindNoun} to draw.";
        return $"{top.ShantenAfter} steps from ready — keeps the most useful draws available.";
    }
}
