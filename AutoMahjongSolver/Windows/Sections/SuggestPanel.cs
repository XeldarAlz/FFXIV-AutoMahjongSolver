using AutoMahjongSolver.Core;
using AutoMahjongSolver.Core.Game;
using AutoMahjongSolver.Engine;
using AutoMahjongSolver.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoMahjongSolver.Windows.Sections;

/// <summary>
/// The suggestion coach. The player makes the move themselves, so this panel is where the tiles live:
/// a hero "discard this" card, the current hand, efficiency metrics and the full discard ranking.
/// </summary>
internal static class SuggestPanel
{
    private const float StatTileHeight = 56f;
    private const float HandTileMinHeight = 30f;
    private const float HandTileMaxHeight = 46f;
    private const float MeldTileMaxHeight = 38f;
    private const float RankCardHeight = 68f;
    private const float RankCardGap = 6f;
    private const float RankDiscardTileHeight = 46f;
    private const float RankAcceptTileHeight = 30f;
    private const int RankCollapsedCount = 3;
    private const int DiscardRankRows = 8;
    private const float DecisionCardHeight = 126f;
    private const float DecisionTileHeight = 58f;
    private const float WinBannerHeight = 78f;

    public static void Draw(LiveBoard board)
    {
        DrawDecision(board);

        if (board.HandRead)
        {
            Styling.VSpace(12);
            DrawHand(board.Snapshot);
            Styling.VSpace(10);
            DrawMetrics(board.Report);

            if (board.Phase == BoardPhase.OurTurn
                && board.Report.Shape == HandShape.Discard)
            {
                Styling.VSpace(12);
                DrawDiscardRanking(board.Report.Discards, board.BestDiscard);
            }
        }
        else
        {
            Styling.VSpace(8);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextWrapped("Reading the board… if this persists, run /amjs dump and check the node ids.");
        }
    }

    // ---- Decision ----------------------------------------------------------

    private static void DrawDecision(LiveBoard board)
    {
        switch (board.Phase)
        {
            case BoardPhase.OurTurn when board.HandComplete:
                DrawWinBanner("TSUMO", "complete hand — declare tsumo");
                break;
            case BoardPhase.OurTurn when board.BestDiscard >= 0:
                DrawDiscardCard(board.BestDiscard);
                break;
            case BoardPhase.OurTurn:
                DrawMiniDecision("Reading the hand", "waiting for the full draw", FontAwesomeIcon.Hourglass, Styling.AccentBlue);
                break;
            case BoardPhase.CallOffered when board.RonOffered:
                DrawWinBanner("RON", "winning tile offered — call Ron");
                break;
            case BoardPhase.CallOffered:
                DrawMiniDecision("Pass", "safe to pass this call", FontAwesomeIcon.Times, Styling.TextDim);
                break;
            case BoardPhase.BetweenHands:
                DrawMiniDecision("Advance", "clear the screen to continue", FontAwesomeIcon.Forward, Styling.AccentBlue);
                break;
            default:
                DrawMiniDecision("Reading the table", "waiting for your turn", FontAwesomeIcon.Eye, Styling.AccentBlueSoft);
                break;
        }
    }

    /// <summary>The hero recommendation: a violet-tinted card holding the single tile to throw.</summary>
    private static void DrawDiscardCard(int tile)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var height = DecisionCardHeight * scale;
        var max = origin + new Vector2(avail, height);

        drawList.AddRectFilled(origin, max,
            ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, Styling.Accent, 0.10f)), Styling.CardRounding);
        drawList.AddRect(origin, max,
            ImGui.GetColorU32(Styling.WithAlpha(Styling.Accent, 0.35f)), Styling.CardRounding, ImDrawFlags.None, 1f * scale);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + 10f * scale));
        Styling.TextCentered("DISCARD THIS", Styling.AccentSoft, 0.92f);

        var tileWidth = TileGlyph.Width(DecisionTileHeight * scale);
        ImGui.SetCursorScreenPos(new Vector2(origin.X + (avail - tileWidth) * 0.5f, origin.Y + 34f * scale));
        TileGlyph.Draw(tile, DecisionTileHeight, aka: false, glow: Styling.Accent);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, max.Y - 24f * scale));
        Styling.TextCentered($"{Tiles.Name(tile)}  ·  highest-efficiency discard", Styling.TextDim, 0.88f);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, height));
    }

    private static void DrawWinBanner(string label, string sub)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var height = WinBannerHeight * scale;
        var min = origin;
        var max = origin + new Vector2(avail, height);
        var pulse = Styling.Pulse(Styling.PulseBreath);

        for (var i = 3; i >= 1; i--)
        {
            var grow = i * 3f * scale;
            drawList.AddRectFilled(min - new Vector2(grow, grow), max + new Vector2(grow, grow),
                ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentAmber, 0.05f * i * (0.6f + 0.4f * pulse))), Styling.CardRounding + grow);
        }

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(Vector4.Lerp(Styling.CardBg, Styling.AccentAmber, 0.18f)), Styling.CardRounding);
        drawList.AddRect(min, max, ImGui.GetColorU32(Styling.WithAlpha(Styling.AccentAmber, 0.55f + 0.35f * pulse)),
            Styling.CardRounding, ImDrawFlags.None, 1.5f * scale);

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + 12f * scale));
        DrawCenteredIcon(FontAwesomeIcon.Trophy, Styling.AccentAmberSoft, 1.35f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + 30f * scale));
        Styling.TextCentered(label, Styling.AccentAmberSoft, 1.6f);
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height - 22f * scale));
        Styling.TextCentered(sub, Styling.TextDim, 0.88f);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, height));
    }

    private static void DrawMiniDecision(string label, string sub, FontAwesomeIcon icon, Vector4 accent)
    {
        Styling.VSpace(2);
        DrawCenteredIcon(icon, accent, 1.2f);
        Styling.VSpace(3);
        Styling.TextCentered(label, Styling.TextStrong, 1.1f);
        Styling.VSpace(1);
        Styling.TextCentered(sub, Styling.TextDim, 0.9f);
    }

    // ---- Hand --------------------------------------------------------------

    private static void DrawHand(HandSnapshot snapshot)
    {
        if (snapshot.Counts is not { Length: Tiles.TypeCount })
            return;

        var hand = ExpandHand(snapshot.Counts, snapshot.AkaCount);
        if (hand.Count == 0)
            return;

        var handHeight = FitTileHeight(hand.Count, HandTileMinHeight, HandTileMaxHeight);
        DrawTileRow(hand, handHeight);

        if (snapshot.MeldCount > 0)
        {
            Styling.VSpace(4);
            var meldTiles = new List<(int Tile, bool Aka)>();
            foreach (Meld meld in snapshot.Melds)
                foreach (int tile in meld.Tiles)
                    meldTiles.Add((tile, false));
            DrawTileRow(meldTiles, MathF.Min(handHeight, MeldTileMaxHeight));
        }
    }

    /// <summary>The tallest tile height (logical px) that lets <paramref name="count"/> tiles span the
    /// available content width, clamped to [min, max] so they stay readable but never overflow a narrow window.</summary>
    private static float FitTileHeight(int count, float minHeight, float maxHeight)
    {
        if (count <= 0)
            return minHeight;

        var scale = ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail().X;
        var gap = 3f * scale;
        var widthBudget = avail - count * (3f * scale) - MathF.Max(0, count - 1) * gap;
        var heightPx = widthBudget / count / TileGlyph.Aspect;
        return Math.Clamp(heightPx / scale, minHeight, maxHeight);
    }

    private static List<(int Tile, bool Aka)> ExpandHand(int[] counts, int akaCount)
    {
        var tiles = new List<(int Tile, bool Aka)>();
        var akaLeft = akaCount;
        for (var t = 0; t < Tiles.TypeCount; t++)
        {
            var isFive = t is 4 or 13 or 22;
            for (var c = 0; c < counts[t]; c++)
            {
                var aka = isFive && akaLeft > 0;
                if (aka) akaLeft--;
                tiles.Add((t, aka));
            }
        }

        return tiles;
    }

    private static void DrawTileRow(IReadOnlyList<(int Tile, bool Aka)> tiles, float height)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 3f * scale;
        var footprint = TileGlyph.Width(height * scale) + 3f * scale;
        var total = tiles.Count * footprint + MathF.Max(0, tiles.Count - 1) * gap;

        Styling.CenterNextItem(total);
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0, gap);
            TileGlyph.Draw(tiles[i].Tile, height, tiles[i].Aka);
        }
    }

    // ---- Metrics -----------------------------------------------------------

    private static void DrawMetrics(HandReport report)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 8f * scale;
        var avail = ImGui.GetContentRegionAvail().X;
        var tileWidth = (avail - gap * 2f) / 3f;
        var size = new Vector2(tileWidth, StatTileHeight * scale);

        var complete = report.Shanten == ShantenCalculator.Complete;
        var tenpai = report.Shanten == 0;

        string shantenValue, shantenCaption, shantenTip;
        string? shantenSub;
        Vector4 shantenAccent;
        if (complete)
        {
            (shantenValue, shantenCaption, shantenSub, shantenAccent) = ("win", "complete", null, Styling.AccentAmber);
            shantenTip = "Your hand is already complete — a winning hand.";
        }
        else if (tenpai)
        {
            (shantenValue, shantenCaption, shantenSub, shantenAccent) = ("0", "ready now", "tenpai", Styling.AccentSoft);
            shantenTip = "Tenpai — your hand is ready; just one more tile completes it.";
        }
        else
        {
            (shantenValue, shantenCaption, shantenSub, shantenAccent) = (report.Shanten.ToString(), "from ready", "shanten", Styling.Accent);
            shantenTip = $"Shanten — {report.Shanten} useful tile swap{(report.Shanten == 1 ? "" : "s")} until your hand is ready to win (tenpai). Lower is closer.";
        }

        var ukeire = report.Shape switch
        {
            HandShape.Discard when report.Discards.Count > 0 => report.Discards[0].UkeireCount,
            HandShape.Wait => report.Wait.Count,
            _ => 0,
        };
        const string ukeireTip = "Ukeire — how many tiles still in play would improve your hand toward a win. Higher means more ways to advance.";

        StatTile.Draw(FontAwesomeIcon.LayerGroup, report.Total.ToString(), "in hand", Styling.AccentBlue, size,
            tooltip: "How many tiles are in your hand right now.");
        ImGui.SameLine(0, gap);
        StatTile.Draw(FontAwesomeIcon.Bullseye, shantenValue, shantenCaption, shantenAccent, size,
            subtitle: shantenSub, tooltip: shantenTip);
        ImGui.SameLine(0, gap);
        StatTile.Draw(FontAwesomeIcon.ArrowUp, ukeire.ToString(), "useful tiles", Styling.AccentMintSoft, size,
            subtitle: "ukeire", tooltip: ukeireTip);
    }

    // ---- Discard ranking ---------------------------------------------------

    private static bool showAllDiscards;

    private static void DrawDiscardRanking(IReadOnlyList<DiscardOption> options, int bestTile)
    {
        if (options.Count == 0)
            return;

        var cap = Math.Min(options.Count, DiscardRankRows);
        var collapsed = Math.Min(RankCollapsedCount, cap);
        var hasMore = cap > collapsed;

        Styling.SectionLabel("Discard ranking");
        if (hasMore)
        {
            ImGui.SameLine();
            var label = showAllDiscards ? $"show top {collapsed}" : $"+{cap - collapsed} more";
            var width = ImGui.CalcTextSize(label).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - width));
            if (ToggleLink(label, Styling.Accent))
                showAllDiscards = !showAllDiscards;
        }

        Styling.VSpace(5);

        var limit = showAllDiscards && hasMore ? cap : collapsed;
        for (var i = 0; i < limit; i++)
        {
            if (i > 0)
                Styling.VSpace(RankCardGap);
            DrawRankCard(options[i], i, options[i].Tile == bestTile);
        }
    }

    /// <summary>One candidate discard as a full-width card: rank, a large discard tile, the metrics and the accept tiles.</summary>
    private static void DrawRankCard(DiscardOption opt, int index, bool isBest)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var height = RankCardHeight * scale;
        var min = origin;
        var max = origin + new Vector2(avail, height);
        var pad = 12f * scale;

        var bg = isBest ? Vector4.Lerp(Styling.CardBg, Styling.Accent, 0.12f) : Styling.CardBgSoft;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(bg), Styling.CardRounding);
        dl.AddRect(min, max, ImGui.GetColorU32(isBest ? Styling.WithAlpha(Styling.Accent, 0.5f) : Styling.BorderDim),
            Styling.CardRounding, ImDrawFlags.None, 1f * scale);

        var rank = $"#{index + 1}";
        ImGui.SetCursorScreenPos(new Vector2(min.X + pad, min.Y + (height - ImGui.GetTextLineHeight()) * 0.5f));
        using (ImRaii.PushColor(ImGuiCol.Text, isBest ? Styling.AccentSoft : Styling.TextMuted))
            ImGui.TextUnformatted(rank);

        var tileHpx = RankDiscardTileHeight * scale;
        var tileWpx = TileGlyph.Width(tileHpx);
        var tileX = min.X + 44f * scale;
        ImGui.SetCursorScreenPos(new Vector2(tileX, min.Y + (height - tileHpx) * 0.5f));
        TileGlyph.Draw(opt.Tile, RankDiscardTileHeight, aka: false, glow: isBest ? Styling.Accent : null);

        var contentX = tileX + tileWpx + 14f * scale;
        ImGui.SetCursorScreenPos(new Vector2(contentX, min.Y + 13f * scale));
        DrawMetricLine(opt);
        DrawAcceptRow(opt.Accepts, contentX, min.Y + 34f * scale);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(avail, height));
    }

    /// <summary>The one-line "2 from ready · 15 useful tiles" readout, numbers coloured, words muted.</summary>
    private static void DrawMetricLine(DiscardOption opt)
    {
        var scale = ImGuiHelpers.GlobalScale;

        if (opt.Shanten == ShantenCalculator.Complete)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentAmberSoft))
                ImGui.TextUnformatted("winning hand");
            return;
        }

        if (opt.Shanten == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentSoft))
                ImGui.TextUnformatted("ready now");
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextStrong))
                ImGui.TextUnformatted(opt.Shanten.ToString());
            ImGui.SameLine(0, 4f * scale);
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
                ImGui.TextUnformatted("from ready");
        }

        ImGui.SameLine(0, 8f * scale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
            ImGui.TextUnformatted("·");
        ImGui.SameLine(0, 8f * scale);

        using (ImRaii.PushColor(ImGuiCol.Text, Styling.AccentMintSoft))
            ImGui.TextUnformatted(opt.UkeireCount.ToString());
        ImGui.SameLine(0, 4f * scale);
        using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextDim))
            ImGui.TextUnformatted("useful tiles");
    }

    private static void DrawAcceptRow(IReadOnlyList<AcceptTile> accepts, float x, float y)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = 3f * scale;
        var shown = Math.Min(accepts.Count, 8);

        ImGui.SetCursorScreenPos(new Vector2(x, y));
        for (var i = 0; i < shown; i++)
        {
            if (i > 0) ImGui.SameLine(0, gap);
            TileGlyph.Draw(accepts[i].Tile, RankAcceptTileHeight);
        }

        if (accepts.Count > shown)
        {
            ImGui.SameLine(0, 5f * scale);
            ImGui.AlignTextToFramePadding();
            using (ImRaii.PushColor(ImGuiCol.Text, Styling.TextMuted))
                ImGui.TextUnformatted($"+{accepts.Count - shown}");
        }
    }

    /// <summary>A small inline clickable label (used for the "show all / show top N" ranking toggle).</summary>
    private static bool ToggleLink(string text, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text);
        var clicked = ImGui.InvisibleButton(text, size);
        var hovered = ImGui.IsItemHovered();
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(hovered ? Styling.AccentSoft : color), text);
        return clicked;
    }

    private static void DrawCenteredIcon(FontAwesomeIcon icon, Vector4 color, float fontScale)
    {
        var iconString = icon.ToIconString();
        ImGui.SetWindowFontScale(fontScale);
        Vector2 size;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            size = ImGui.CalcTextSize(iconString);

        Styling.CenterNextItem(size.X);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextUnformatted(iconString);
        ImGui.SetWindowFontScale(1f);
    }
}
