using System.Numerics;
using Dalamud.Bindings.ImGui;
using Mahjong.Plugin.Dalamud.GameState;

namespace Mahjong.Plugin.Dalamud.UI.DebugTabs;

/// <summary>AddonEmj lifecycle, plus the active variant and layout profile.</summary>
internal sealed class AddonTab
{
    private readonly DevConsoleContext ctx;

    public AddonTab(DevConsoleContext ctx) => this.ctx = ctx;

    public void Draw()
    {
        using (Theme.BeginCard("addon"))
        {
            Theme.SectionHeader("AddonEmj");
            Theme.Subtle("In-memory address and visibility of the mahjong UI. Empty until you seat at a table.");
            var obs = ctx.Plugin.AddonReader.Poll();
            if (!obs.Present)
            {
                var candidates = string.Join("\" or \"", MahjongAddon.CandidateNames);
                Theme.Subtle($"Addon \"{candidates}\" not found. Open a Doman Mahjong match in-game.");
                if (obs.LastLifecycleEvent is not null)
                {
                    ImGui.Dummy(new Vector2(0, 4));
                    DevHelpers.KeyValueRow("Last event", obs.LastLifecycleEvent);
                }
                return;
            }

            DevHelpers.KeyValueRow("Address", $"0x{obs.Address:X}");
            DevHelpers.KeyValueRow("Visible", obs.IsVisible.ToString());
            DevHelpers.KeyValueRow("Last event", obs.LastLifecycleEvent ?? "(none)");
        }

        ImGui.Dummy(new Vector2(0, 4));
        DrawVariantCard();
        ImGui.Dummy(new Vector2(0, 4));
        DrawLayoutCard();
    }

    private void DrawVariantCard()
    {
        using (Theme.BeginCard("addon-variants"))
        {
            Theme.SectionHeader("Variant probes");
            Theme.Subtle("Which variant strategy matched on the current addon. Miss on all = run Bug report → Dump variant.");

            var selector = ctx.Plugin.AddonReader.Selector;
            if (selector.Variants.Count == 0)
            {
                Theme.Subtle("(no variants registered)");
                return;
            }

            foreach (var v in selector.Variants)
            {
                string status = ProbeStatus(v);
                DevHelpers.KeyValueRow(v.Name, status);
            }
        }
    }

    private unsafe string ProbeStatus(GameState.Variants.IEmjVariant v)
    {
        if (!ctx.Addon.TryGet(out var unit, out _))
            return "no addon";
        try
        {
            return v.Probe(unit) ? "MATCH" : "miss";
        }
        catch (System.Exception ex)
        {
            return $"threw: {ex.GetType().Name}";
        }
    }

    private void DrawLayoutCard()
    {
        using (Theme.BeginCard("addon-layout"))
        {
            Theme.SectionHeader("Active layout profile");
            Theme.Subtle("Read-offsets the variant uses to extract your hand, scores, and dora from addon memory.");

            var layout = ctx.Plugin.AddonReader.ActiveLayout;
            if (layout is null)
            {
                Theme.Subtle("(no profile active — addon hasn't been parsed yet)");
                return;
            }

            DevHelpers.KeyValueRow("Name", layout.Name);
            DevHelpers.KeyValueRow("Addon name", layout.AddonName);
            DevHelpers.KeyValueRow("Tile tex base", layout.TileTextureBase.ToString());
            DevHelpers.KeyValueRow("Hand array start", $"0x{layout.Offsets.HandArrayStart:X}");
            DevHelpers.KeyValueRow("Self score", $"0x{layout.Offsets.SelfScore:X}");
            DevHelpers.KeyValueRow("Dora indicator", $"0x{layout.Offsets.DoraIndicator:X}");
            DevHelpers.KeyValueRow("Hand size limit", layout.Limits.HandSize.ToString());
        }
    }
}
