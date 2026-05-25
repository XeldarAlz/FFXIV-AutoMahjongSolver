using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Policy.Abstractions;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>Manual dispatch surface: auto-discard, test slot, test call option.</summary>
internal sealed class ActionsTab
{
    private readonly DevConsoleContext ctx;
    private int testDiscardSlot = 13;
    private int testCallOption;

    public ActionsTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        var snap = ctx.Plugin.Aggregator.Latest;
        bool addonPresent = snap is not null;
        bool ourTurn = addonPresent && snap!.Legal.Can(ActionFlags.Discard);

        using (Theme.BeginCard("actions-auto"))
        {
            Theme.SectionHeader("Auto-discard");
            Theme.Subtle("Asks the policy what to discard and dispatches the click. Only works on your turn.");
            using (DevHelpers.Disable(!ourTurn))
            {
                float w = ImGui.GetContentRegionAvail().X;
                if (ImGui.Button("Run policy pick", new Vector2(w, 34)))
                    ctx.Framework.RunOnFrameworkThread(TriggerAutoDiscard);
            }
            if (!ourTurn)
                Theme.Subtle(addonPresent ? "Not our turn." : "No snapshot — open a match first.");
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("actions-testslot"))
        {
            Theme.SectionHeader("Test discard slot");
            Theme.Subtle("Discards the tile at a specific hand slot regardless of policy. 0 = leftmost, 13 = just-drawn.");

            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("##slot", ref testDiscardSlot);
            testDiscardSlot = Math.Clamp(testDiscardSlot, 0, 13);
            ImGui.SameLine(0, 8);
            using (DevHelpers.Disable(!ourTurn))
            {
                if (ImGui.Button($"Dispatch slot {testDiscardSlot}"))
                {
                    int slot = testDiscardSlot;
                    ctx.Framework.RunOnFrameworkThread(() =>
                    {
                        var r = ctx.Plugin.Dispatcher.DispatchDiscard(slot);
                        ctx.LastToast = $"discard slot={slot} → {r}";
                    });
                }
            }

            ImGui.Dummy(new Vector2(0, 3));
            if (ImGui.SmallButton("drawn (13)"))
                testDiscardSlot = 13;
            ImGui.SameLine(0, 4);
            if (ImGui.SmallButton("leftmost (0)"))
                testDiscardSlot = 0;
        }

        ImGui.Dummy(new Vector2(0, 4));
        using (Theme.BeginCard("actions-testcall"))
        {
            Theme.SectionHeader("Test call option");
            Theme.Subtle("Send a raw call-prompt button index. Enable event-logger in Diagnostics to see which button each index hits.");

            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("##opt", ref testCallOption);
            testCallOption = Math.Clamp(testCallOption, 0, 5);
            ImGui.SameLine(0, 8);
            if (ImGui.Button($"Dispatch opt {testCallOption}"))
            {
                int opt = testCallOption;
                ctx.Framework.RunOnFrameworkThread(() =>
                {
                    var r = ctx.Plugin.Dispatcher.DispatchCallOption(opt);
                    ctx.LastToast = $"call opt={opt} → {r}";
                });
            }

            ImGui.Dummy(new Vector2(0, 3));
            for (int i = 0; i < 3; i++)
            {
                if (i > 0)
                    ImGui.SameLine(0, 4);
                int v = i;
                if (ImGui.SmallButton(v.ToString()))
                    testCallOption = v;
            }

            ImGui.Dummy(new Vector2(0, 3));
            Theme.Subtle("pon/pass: 0=Pass, 1=Pon.  chi/pass: likely 0=Chi, 1=Pass.  Verify by logging.");
        }
    }

    private void TriggerAutoDiscard()
    {
        var snap = ctx.Plugin.AddonReader.TryBuildSnapshot();
        if (snap is null || !snap.Legal.Can(ActionFlags.Discard))
        {
            ctx.LastToast = "auto-discard: not our turn";
            return;
        }

        var choice = ctx.Plugin.Policy.Choose(snap);
        if (choice.Kind != ActionKind.Discard || choice.DiscardTile is null)
        {
            ctx.LastToast = $"auto-discard: policy returned {choice.Kind}";
            return;
        }

        var tile = choice.DiscardTile.Value;
        int slot = ctx.Plugin.AddonReader.FindAddonSlotOfTile(tile);
        if (slot < 0)
        {
            ctx.LastToast = $"auto-discard: tile {tile} not found in hand";
            return;
        }

        var delay = HumanTiming.RandomDelay();
        ctx.LastToast = $"auto-discarding {tile} slot {slot} in {delay.TotalMilliseconds:F0}ms";

        _ = ctx.Framework.RunOnTick(() =>
        {
            var r = ctx.Plugin.Dispatcher.DispatchDiscard(slot);
            ctx.LastToast = $"auto-discarded {tile} → {r}";
        }, delay);
    }
}
