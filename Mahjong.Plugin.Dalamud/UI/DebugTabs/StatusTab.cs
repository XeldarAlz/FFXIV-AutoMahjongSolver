using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Policy.Abstractions;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Read-only live readouts: flags, loop, hook health, call-prompt analysis, top picks.</summary>
internal sealed class StatusTab
{
    private readonly DevConsoleContext ctx;

    public StatusTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        var plugin = ctx.Plugin;

        using (Theme.BeginCard("status-loop"))
        {
            Theme.SectionHeader("Auto-play loop");
            Theme.Subtle("What the auto-play loop saw last tick. Updates live during a match.");
            DevHelpers.KeyValueRow("State", plugin.AutoPlay.LastObservedState.ToString());
            DevHelpers.KeyValueRow("Hand", plugin.AutoPlay.LastObservedHandCount.ToString());
            DevHelpers.KeyValueRow("Last", plugin.AutoPlay.LastActionDescription);
            ImGui.Dummy(new Vector2(0, 4));
            DrawAtkValuesRow();
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("status-hooks"))
        {
            Theme.SectionHeader("Hooks & telemetry");
            Theme.Subtle("Health of the discard-capture hook and ongoing data-collection counters.");
            var capture = plugin.DiscardCapture;
            DevHelpers.KeyValueRow(
                "DiscardCapture",
                $"{capture.StrategyName}  health={capture.Health}  " +
                $"total={capture.TotalCaptured}  lastTile={capture.LastTileId}");

            DevHelpers.KeyValueRow(
                "Autosnap",
                plugin.MjAutoCommand.IsAutoSnapOn
                    ? $"ON  ({plugin.MjAutoCommand.AutoSnapCount}/{plugin.MjAutoCommand.AutoSnapMaxCountValue})"
                    : "OFF");

            DevHelpers.KeyValueRow(
                "Event log",
                plugin.EventLogger.Enabled ? "ON" : "OFF");

            int findingsToday = CountFindingsToday();
            DevHelpers.KeyValueRow("Findings (today)", findingsToday.ToString());
        }

        ImGui.Dummy(new Vector2(0, 4));
        DrawCallPromptCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawPolicyPickCard();
    }

    /// <summary>Live legal-flags and dispatch-index readout for a chi/pon/kan prompt.</summary>
    private void DrawCallPromptCard()
    {
        using (Theme.BeginCard("status-callprompt"))
        {
            Theme.SectionHeader("Call-prompt analysis");
            Theme.Subtle("Lights up during a chi/pon/kan offer. opt= is the button index the plugin would click.");

            var snap = ctx.Plugin.AddonReader.TryBuildSnapshot();
            if (snap is null)
            {
                Theme.Subtle("No snapshot.");
                return;
            }

            var legal = snap.Legal;
            const ActionFlags acceptMask =
                ActionFlags.Pon | ActionFlags.Chi |
                ActionFlags.MinKan | ActionFlags.ShouMinKan |
                ActionFlags.Ron | ActionFlags.Riichi | ActionFlags.Tsumo;
            bool isCallPrompt = (legal.Flags & acceptMask) != 0;

            DevHelpers.KeyValueRow("Flags", legal.Flags.ToString());
            DevHelpers.KeyValueRow("Pon / Chi / Kan candidates",
                $"{legal.PonCandidates.Count} / {legal.ChiCandidates.Count} / {legal.KanCandidates.Count}");
            DevHelpers.KeyValueRow("AkaDora total", snap.AkaDora.ToString());

            if (!isCallPrompt)
            {
                ImGui.Dummy(new Vector2(0, 4));
                Theme.Subtle("No accept-side action on offer — not a call prompt.");
                return;
            }

            ImGui.Dummy(new Vector2(0, 4));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
            ImGui.TextUnformatted("Dispatch indices");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 2));

            void RowFor(ActionKind kind, ActionFlags flag, string label)
            {
                if ((legal.Flags & flag) == 0)
                    return;
                MeldCandidate? sample = kind switch
                {
                    ActionKind.Pon => legal.PonCandidates.Count > 0 ? legal.PonCandidates[0] : (MeldCandidate?)null,
                    ActionKind.Chi => legal.ChiCandidates.Count > 0 ? legal.ChiCandidates[0] : (MeldCandidate?)null,
                    ActionKind.MinKan => legal.KanCandidates.Count > 0 ? legal.KanCandidates[0] : (MeldCandidate?)null,
                    _ => null,
                };
                int idx = AutoPlayLoop.ComputeAcceptIndex(kind, legal, sample);
                DevHelpers.KeyValueRow($"  {label}", $"opt={idx}");
            }
            RowFor(ActionKind.Pon, ActionFlags.Pon, "Pon");
            RowFor(ActionKind.Chi, ActionFlags.Chi,
                legal.ChiCandidates.Count > 1
                    ? $"Chi (×{legal.ChiCandidates.Count} variants)"
                    : "Chi");
            RowFor(ActionKind.MinKan, ActionFlags.MinKan, "MinKan");
            RowFor(ActionKind.ShouMinKan, ActionFlags.ShouMinKan, "ShouMinKan");
            RowFor(ActionKind.Ron, ActionFlags.Ron, "Ron");
            RowFor(ActionKind.Riichi, ActionFlags.Riichi, "Riichi");
            RowFor(ActionKind.Tsumo, ActionFlags.Tsumo, "Tsumo");
            DevHelpers.KeyValueRow("  Pass", $"opt={AutoPlayLoop.ComputePassIndex(legal)}");

            var choice = ctx.Plugin.Policy.Choose(snap);
            ImGui.Dummy(new Vector2(0, 4));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Info);
            ImGui.TextUnformatted($"Policy → {choice.Kind} {(choice.DiscardTile?.ToString() ?? "")}");
            ImGui.PopStyleColor();
            if (!string.IsNullOrEmpty(choice.Reasoning))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Faint);
                ImGui.TextWrapped($"  {choice.Reasoning}");
                ImGui.PopStyleColor();
            }
        }
    }

    private unsafe void DrawAtkValuesRow()
    {
        string atkLine = "(addon not found)";
        if (ctx.Addon.TryGet(out var unit, out _))
        {
            if (unit->AtkValues != null && unit->AtkValuesCount > 0)
            {
                var v0 = unit->AtkValues[0];
                string v0s = v0.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v0.Int.ToString() : "?";
                string v1s = "-", v2s = "-";
                if (unit->AtkValuesCount > 1)
                {
                    var v1 = unit->AtkValues[1];
                    v1s = v1.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v1.Int.ToString() : v1.Type.ToString();
                }
                if (unit->AtkValuesCount > 2)
                {
                    var v2 = unit->AtkValues[2];
                    v2s = v2.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v2.Int.ToString() : v2.Type.ToString();
                }
                atkLine = $"{v0s}   {v1s}   {v2s}";
            }
        }
        DevHelpers.KeyValueRow("AtkValues [0..2]", atkLine);
    }

    private void DrawPolicyPickCard()
    {
        using (Theme.BeginCard("status-pick"))
        {
            Theme.SectionHeader("Suggestions (discard-only)");
            Theme.Subtle("Top 5 discard candidates ranked by shanten / ukeire / dora. Highlighted tile is the policy pick.");

            var snap = ctx.Plugin.AddonReader.TryBuildSnapshot();
            if (snap is null)
            {
                Theme.Subtle("No snapshot — not in a match, or the addon struct is not readable.");
                return;
            }

            Theme.Caption($"Hand · {snap.Hand.Count} tiles");
            ImGui.Dummy(new Vector2(0, 2));
            Theme.DrawHand(snap.Hand);

            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
            ImGui.TextUnformatted(
                $"Scores   you: {snap.Scores[0]}    right: {snap.Scores[1]}    " +
                $"across: {snap.Scores[2]}    left: {snap.Scores[3]}");
            ImGui.PopStyleColor();

            if (!snap.Legal.Can(ActionFlags.Discard))
            {
                ImGui.Dummy(new Vector2(0, 4));
                Theme.Subtle($"Waiting for our turn ({snap.Hand.Count} tiles).");
                return;
            }

            ScoredDiscard[] scored;
            try
            { scored = DiscardScorer.Score(snap); }
            catch (Exception ex)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Danger);
                ImGui.TextWrapped($"scorer error: {ex.Message}");
                ImGui.PopStyleColor();
                return;
            }

            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Header);
            ImGui.TextUnformatted("Top picks");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 4));

            int show = Math.Min(5, scored.Length);
            for (int i = 0; i < show; i++)
                DrawPickRow(i, scored[i]);

            var choice = ctx.Plugin.Policy.Choose(snap);
            ImGui.Dummy(new Vector2(0, 6));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Info);
            ImGui.TextUnformatted($"Policy pick:  {choice.Kind} {(choice.DiscardTile?.ToString() ?? "")}");
            ImGui.PopStyleColor();
            if (!string.IsNullOrEmpty(choice.Reasoning))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Faint);
                ImGui.TextWrapped($"  {choice.Reasoning}");
                ImGui.PopStyleColor();
            }
        }
    }

    private static void DrawPickRow(int rank, ScoredDiscard s)
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
        ImGui.TextUnformatted(
            $"shanten={s.ShantenAfter}   ukeire={s.UkeireKinds}k/{s.UkeireWeighted}w   " +
            $"dora={s.DoraRetained}   yaku={s.YakuhaiRetained}   score={s.Score:F1}");
        ImGui.PopStyleColor();

        ImGui.SetCursorPosY(rowStart + tileH + 3);
    }

    /// <summary>Line-count today's findings NDJSON, or -1 if the file isn't readable.</summary>
    private static int CountFindingsToday()
    {
        try
        {
            var dir = Path.Combine(
                Plugin.PluginInterface.GetPluginConfigDirectory(),
                "findings");
            var path = Path.Combine(dir, $"findings-{DateTime.UtcNow:yyyyMMdd}.ndjson");
            if (!File.Exists(path))
                return 0;
            int n = 0;
            using var r = new StreamReader(new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            while (r.ReadLine() != null)
                n++;
            return n;
        }
        catch
        {
            return -1;
        }
    }
}
