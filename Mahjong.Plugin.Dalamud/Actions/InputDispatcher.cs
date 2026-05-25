using System;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Game.Variants;

namespace Mahjong.Plugin.Dalamud.Actions;

/// <summary>
/// Sends input events to the <c>Emj</c> addon via <c>AtkUnitBase.FireCallback</c>.
/// All calls must be made from the framework thread.
///
/// Callback patterns discovered during M6 logging (see <c>memory/project_addon_emj_re_notes.md</c>):
/// <list type="bullet">
///   <item><description>Discard tile at slot N (0-13): <c>FireCallback([Int=7, Int=N])</c></description></item>
///   <item><description>Pass on a call prompt:      <c>FireCallback([Int=11, Int=0])</c></description></item>
/// </list>
/// Pon/Chi/Kan/Riichi/Tsumo/Ron patterns are still unmapped — need a logging session
/// where the user actually triggers those actions.
/// </summary>
public sealed class InputDispatcher
{
    // Fallback values match data/layouts/{emj,emj_l}.json. Used when the layout accessor isn't wired (tests) or hasn't resolved yet (no addon attached).
    private const int DefaultSelfDeclareListCode = 6;
    private const int DefaultOurTurnDiscardCode = 30;
    private const int DefaultHandArrayStartOffset = 0x0DB8;

    private readonly MahjongAddon addon;
    private readonly Func<LayoutProfile?>? layoutAccessor;

    public InputDispatcher(MahjongAddon addon, Func<LayoutProfile?>? layoutAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(addon);
        this.addon = addon;
        this.layoutAccessor = layoutAccessor;
    }

    private int SelfDeclareListCode =>
        layoutAccessor?.Invoke()?.StateCodes.SelfDeclareList ?? DefaultSelfDeclareListCode;
    private int OurTurnDiscardCode =>
        layoutAccessor?.Invoke()?.StateCodes.OurTurnDiscard ?? DefaultOurTurnDiscardCode;
    private int HandArrayStartOffset =>
        layoutAccessor?.Invoke()?.Offsets.HandArrayStart ?? DefaultHandArrayStartOffset;

    public enum DispatchResult
    {
        Ok,
        AddonNotFound,
        AddonNotVisible,
        InvalidSlot,
        HookFailed,         // FireCallback returned false (wrong state / invalid args)
    }

    /// <summary>
    /// Records which click path the most recent <see cref="DispatchDiscard"/> took
    /// (opcode-15 tile-click, list-widget SelectItem, opcode-7 slot-discard, or
    /// the bail reasons). The discard dispatcher has three viable paths and the
    /// uniform <c>DispatchResult.Ok</c> return value can't tell them apart —
    /// every shipped path returns Ok even when the game silently no-ops the
    /// click. AutoPlayLoop reads this to annotate <c>dispatch_attempted</c>
    /// findings so the next stall is unambiguous in the corpus.
    /// </summary>
    public string LastDiscardPath { get; private set; } = "(none)";

    /// <summary>
    /// Discard the tile at the given closed-hand slot (0..13). Slot 13 = last-drawn tile.
    ///
    /// <para><b>The discard protocol is a TWO-callback handshake</b> — verified by
    /// capturing a real manual user discard via the FireCallback hook on
    /// 2026-05-23:</para>
    ///
    /// <code>
    ///   11:15:11.659  FireCallback [15, textureId]  ← select tile (highlights + dismisses popup)
    ///   11:15:11.659  state transitions to 30 (if it wasn't already)
    ///   11:15:11.972  FireCallback [7,  slotIndex]  ← commit (discards the selected tile)
    ///   11:15:11.995  hand updates (tile gone)
    ///   </code>
    ///
    /// <para>Either call alone does <i>nothing</i> committed: opcode-15 only
    /// sets an internal "selected tile" marker and dismisses the self-declare
    /// popup, while opcode-7 only commits whatever was previously selected.
    /// The bot used to fire only one of them depending on state code, which
    /// is why dispatches reported <c>Ok</c> for months but tiles never left
    /// the user's hand — captured 2026-05-23 in the dev-build trial as 14:00
    /// onwards: <c>[15, raw]</c> at state=6, <c>[7, slot]</c> at state=30,
    /// neither committing because they were never paired.</para>
    ///
    /// <para>Both callbacks always fire — back-to-back, synchronously, no
    /// inter-call delay. State-6→30 transition happens inside opcode-15's
    /// internal handler, so opcode-7 sees the correct state by the time it
    /// runs. The 313 ms gap in the manual capture is just mouse-down vs.
    /// mouse-up timing, not a required interval.</para>
    ///
    /// <para><b>List-widget post-call branch</b> (<see cref="TryDispatchListItemClick"/>)
    /// covers state-6 with hand != 14 (post-pon/chi discard popup) and
    /// state-28 (CallPromptList novice-table popup). Those use the
    /// AtkComponentList SelectItem vfunc instead of the two-callback
    /// handshake — verified across post-chi/pon discard popups 2026-05-10..05-18.</para>
    /// </summary>
    public unsafe DispatchResult DispatchDiscard(int slotIndex)
    {
        if (slotIndex is < 0 or > 13)
        {
            LastDiscardPath = "invalid-slot";
            return DispatchResult.InvalidSlot;
        }

        if (!addon.TryGet(out var unit, out _))
        {
            LastDiscardPath = "addon-not-found";
            return DispatchResult.AddonNotFound;
        }
        if (!unit->IsVisible)
        {
            LastDiscardPath = "addon-not-visible";
            return DispatchResult.AddonNotVisible;
        }

        int stateCode = ReadStateCode(unit);
        int handCount = ReadCurrentHandCount(unit);

        // Two-callback discard handshake — see method docstring for the
        // capture-verified protocol. Fires for any deal-shape closed hand
        // (count % 3 == 2 → 14, 11, 8, 5, 2 tiles depending on prior calls)
        // at the two discard-eligible state codes:
        //   - state-6 hand=14: self-declare-after-draw popup; opcode-15
        //     dismisses popup AND selects, opcode-7 commits.
        //   - state-6 hand=11/8/5: post-pon/chi discard popup; same handshake
        //     against the (smaller) closed hand. The earlier list-widget
        //     SelectItem path silently no-opped here — captured 2026-05-23
        //     in the dev-build trial when accepting a pon left the bot
        //     stuck at hand=11 with no commit.
        //   - state-30 hand=14: classic ourTurnDiscard surface; opcode-15
        //     selects, opcode-7 commits.
        bool isTileClickDiscard = handCount > 0
            && handCount % 3 == 2
            && (stateCode == SelfDeclareListCode
                || stateCode == OurTurnDiscardCode);
        if (isTileClickDiscard)
        {
            int raw = ReadHandSlotRaw(unit, slotIndex);
            if (raw > 0)
            {
                var v15 = stackalloc AtkValue[2];
                v15[0].SetInt(15);
                v15[1].SetInt(raw);
                unit->FireCallback(2, v15, true);

                var v7 = stackalloc AtkValue[2];
                v7[0].SetInt(7);
                v7[1].SetInt(slotIndex);
                unit->FireCallback(2, v7, true);

                LastDiscardPath = $"opcode-15+7(raw={raw},slot={slotIndex})";
                return DispatchResult.Ok;
            }
        }

        // Post-call list-widget paths (state-6 hand=11/8/5, state-28 chi list).
        // The list items here ARE the discardable surface and SelectItem
        // commits cleanly (verified across post-chi/pon discard popups
        // 2026-05-10..05-18). SelectItem is void so we can't capture a
        // game-side ack; report Ok and rely on the FSM context-suppression
        // plus the snapshot-derived hand-shrink signal to gate retries.
        if (IsListWidgetPopupActive(unit) && TryDispatchListItemClick(unit, slotIndex))
        {
            LastDiscardPath = $"list-widget(slot={slotIndex})";
            return DispatchResult.Ok;
        }

        // Last-resort fallback: fire just opcode-7 in case we landed on an
        // un-mapped state where the tile-click handshake doesn't apply.
        // Returns HookFailed honestly so the FSM clears context and we don't
        // retry the same dead path for 3 seconds.
        var values = stackalloc AtkValue[2];
        values[0].SetInt(7);
        values[1].SetInt(slotIndex);
        bool ok = unit->FireCallback(2, values, true);
        LastDiscardPath = $"opcode-7-fallback(slot={slotIndex},ok={ok})";
        return ok ? DispatchResult.Ok : DispatchResult.HookFailed;
    }

    private static unsafe int ReadStateCode(AtkUnitBase* unit)
    {
        if (unit->AtkValues == null || unit->AtkValuesCount == 0)
            return -1;
        var v = unit->AtkValues[0];
        return v.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v.Int : -1;
    }

    private unsafe int ReadHandSlotRaw(AtkUnitBase* unit, int slotIndex)
    {
        if (slotIndex is < 0 or > 13)
            return 0;
        byte* basePtr = (byte*)unit;
        return *(int*)(basePtr + HandArrayStartOffset + slotIndex * 4);
    }

    /// <summary>Counts non-zero hand-array slots (0..14). Scans the full array since post-call layouts park the claimed tile at slot 13 with [10..12] empty, so zero-terminating would miscount.</summary>
    private unsafe int ReadCurrentHandCount(AtkUnitBase* unit)
    {
        byte* basePtr = (byte*)unit;
        int offset = HandArrayStartOffset;
        int count = 0;
        for (int i = 0; i < 14; i++)
        {
            int raw = *(int*)(basePtr + offset + i * 4);
            if (raw != 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// True when the call-modal host (node 104) is visible and its inner
    /// shell (node 3) is an AtkComponentList. Distinguishes the list-widget
    /// popups (state-6 SelfDeclareList, state-28 CallPromptList) from the
    /// in-hand discard surface (state-30, no modal node) and from classic
    /// button-row popups (state-15 with string labels).
    /// </summary>
    private static unsafe bool IsListWidgetPopupActive(AtkUnitBase* unit)
    {
        var host = unit->GetNodeById(104);
        if (host == null || (int)host->Type < 1000)
            return false;
        if (!host->IsVisible())
            return false;
        var hostComp = ((AtkComponentNode*)host)->Component;
        if (hostComp == null)
            return false;
        var shell = hostComp->GetNodeById(3);
        return shell != null && (int)shell->Type == 1030;
    }

    /// <summary>
    /// Select option <paramref name="option"/> on the currently-active call prompt.
    /// Option numbers are button-order (leftmost = 0):
    ///   pon/pass prompt:    0 = Pon, 1 = Pass
    ///   chi/pass prompt:    0 = Chi, 1 = Pass
    ///   chi multi-sequence: 0..N = chi variants, N+1 = Pass
    ///   riichi (state 6):   0 = Riichi, 1 = Pass — same payload, different state code
    /// "Pass" is always the RIGHTMOST option.
    ///
    /// <para>Return value note: FireCallback returns <c>false</c> for the call-prompt
    /// opcode (11) even on manual in-game clicks that the game visibly accepts —
    /// verified by capturing pon/chi/riichi/tsumo button presses with the capture
    /// hook, which all logged <c>result=False</c> despite the pon/chi/riichi/tsumo
    /// actually firing. The return value is not a success signal for this opcode, so
    /// we ignore it and always report <see cref="DispatchResult.Ok"/>. The caller is
    /// expected to have verified the modal-visibility gate before dispatching —
    /// that's the real "should we click" predicate.</para>
    /// </summary>
    public unsafe DispatchResult DispatchCallOption(int option)
    {
        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        // Both state-15 classic popups (pon/chi/kan/ron + pass button row) and
        // state-6/28 list-widget popups (standalone Riichi/Pass) share the same
        // AtkComponentList shell type (1030), so the shell-type check alone
        // can't tell them apart. The reliable discriminator is parent AtkValues:
        // state-15 prompts put the button labels ("Pon", "Chi", "Pass", ...) as
        // plain Strings at low indices; state-6/28 prompts put only Ints/Bools
        // there with labels living inside the list items' text nodes.
        //
        // Dispatch accordingly:
        //  - Classic button-row: FireCallback([11, opt]) — what the game's own
        //    click handler ends up firing for a button press.
        //  - List widget: route through the AtkComponentList's SelectItem vfunc
        //    with dispatchEvent: true so the internal CallBackInterface runs
        //    (mouse-up → ListItemClick → commit). FireCallback alone on a list
        //    widget only plays the cosmetic declaration animation without
        //    committing state, which is what broke v0.0.0.16/.17.
        //
        // v0.0.0.18 routed everything through SelectItem and broke state-15
        // (pon/chi/ron) because SelectItem doesn't fire the addon-level opcode-11
        // callback the button-row handler expects. Distinguishing the two cases
        // restores state-15 behavior while keeping the state-6/28 fix.
        if (HasClassicButtonLabels(unit))
        {
            var values = stackalloc AtkValue[2];
            values[0].SetInt(11);
            values[1].SetInt(option);
            unit->FireCallback(2, values, true);
            return DispatchResult.Ok;
        }

        if (TryDispatchListItemClick(unit, option))
            return DispatchResult.Ok;

        // Fallback if the shell isn't a list widget either — keep the legacy
        // FireCallback path so we don't silently drop the dispatch.
        var fallback = stackalloc AtkValue[2];
        fallback[0].SetInt(11);
        fallback[1].SetInt(option);
        unit->FireCallback(2, fallback, true);
        return DispatchResult.Ok;
    }

    /// <summary>
    /// True when parent AtkValues carry a bare button-label string like
    /// "Pon"/"Chi"/"Kan"/"Ron"/"Riichi"/"Tsumo"/"Pass" in the first ~20 slots —
    /// the signature of a state-15 classic button-row popup. State-6/28
    /// list-widget popups carry only Ints/Bools there (labels live inside
    /// list-item children), so a false from this check routes dispatch to the
    /// SelectItem path.
    /// </summary>
    private static unsafe bool HasClassicButtonLabels(AtkUnitBase* unit)
    {
        var atkValues = unit->AtkValues;
        if (atkValues == null)
            return false;
        int scanEnd = Math.Min((int)unit->AtkValuesCount, 20);
        for (int i = 0; i < scanEnd; i++)
        {
            var v = atkValues[i];
            if (v.Type != FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String &&
                v.Type != FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.String8 &&
                v.Type != FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.ManagedString)
                continue;
            if (v.String.Value == null)
                continue;
            var s = v.String.ToString();
            switch (s)
            {
                case "Pon":
                case "Chi":
                case "Kan":
                case "Ron":
                case "Riichi":
                case "Tsumo":
                case "Pass":
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// If the modal shell is an AtkComponentList, dispatch the click through
    /// the list's native <c>SelectItem(index, dispatchEvent: true)</c> — same
    /// code path a mouse-up runs into. Returns true when handled, false when
    /// the shell isn't a list and the caller should fall back.
    /// </summary>
    private static unsafe bool TryDispatchListItemClick(AtkUnitBase* unit, int option)
    {
        var host = unit->GetNodeById(104);
        if (host == null || (int)host->Type < 1000)
            return false;
        var hostComp = ((AtkComponentNode*)host)->Component;
        if (hostComp == null)
            return false;
        var shell = hostComp->GetNodeById(3);
        if (shell == null || (int)shell->Type != 1030)
            return false;
        var shellComp = ((AtkComponentNode*)shell)->Component;
        if (shellComp == null)
            return false;
        var list = (FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentList*)shellComp;
        list->SelectItem(option, dispatchEvent: true);
        return true;
    }

    /// <summary>
    /// Pass on a call prompt. Option 1 = Pass (rightmost button). Confirmed by observation:
    /// pon/pass and chi/pass prompts both show [Call][Pass] order, so pass is always opt 1.
    /// No fallback — if this fails we return HookFailed; fallback to option 0 would
    /// accidentally fire the call action (undesired).
    /// </summary>
    public DispatchResult DispatchPass() => DispatchCallOption(1);

    /// <summary>
    /// Accept a pon/chi/kan call by clicking the leftmost button (option 0). The game
    /// knows from context which call is offered — we just fire option 0. For chi
    /// prompts with multiple sequence variants, option 0 picks the first (lowest)
    /// sequence; we'd need a specific override for non-default variants.
    /// </summary>
    public DispatchResult DispatchCall() => DispatchCallOption(0);

    /// <summary>List-index of <paramref name="target"/> in the rendered hand, for UI highlight callers. Prefers index 13 when hand is full. For addon-slot resolution (dispatch path) use <c>AddonEmjReader.FindAddonSlotOfTile</c>.</summary>
    public static int FindSlotOfTile(Tile target, System.Collections.Generic.IReadOnlyList<Tile> hand)
    {
        if (hand.Count == 14 && hand[13].Id == target.Id)
            return 13;
        for (int i = 0; i < hand.Count; i++)
            if (hand[i].Id == target.Id)
                return i;
        return -1;
    }

    /// <summary>
    /// Opcode constants for FireCallback's first AtkValue. All values here
    /// are corpus-confirmed from the inputs telemetry stream.
    ///
    /// Discard (15+7) is the two-callback handshake captured 2026-05-23.
    /// CallPrompt (11) handles every popup button: Pon, Chi (multi-variant),
    /// MinKan, ShouMinKan, AnKan, Ron, Riichi (declaration click), Tsumo, and
    /// Pass.
    ///
    /// Historical note: opcodes 8 (Riichi), 9 (Tsumo), 10 (Ron), and 12 (Kan)
    /// were shipped as speculative dispatchers across v0.1.0.11..v0.1.1.1.
    /// Field bug #39 (2026-05-24) caught the Ron path corrupting state into
    /// the DRAW screen; bug #40 (2026-05-25) caught the Tsumo path firing
    /// <c>FireCallback(1, [Int=9])</c> 50+ times at state-6 SelfDeclareList
    /// with result=false and zero state movement — same class. The corpus
    /// records for opcode 9 were the addon's own internal callback fired
    /// *after* a SelectItem(0) click on the Tsumo list item, not a click-
    /// equivalent payload our plugin could replay standalone. Ron / AnKan /
    /// ShouMinKan / Riichi declaration / Tsumo now all flow through the
    /// corpus-confirmed call-prompt button-row path (opcode 11) — which
    /// <see cref="DispatchCallOption"/> auto-routes to <c>SelectItem</c> on
    /// list-widget popups (state-6/28) and to <c>FireCallback([11, opt])</c>
    /// on classic button-row popups (state-15).
    /// </summary>
    private static class Opcode
    {
        public const int Discard = 7;
        public const int CallPrompt = 11;
    }

    /// <summary>
    /// Pick a chi variant on the chi-variant-select sub-popup. Opcode 12 +
    /// variant index — captured 2026-05-25 from a manual click on a 2-variant
    /// chi popup (hand=4778m123568p225s, fire_args=[12, 0], state=30). The
    /// popup's parent AtkValues carry a "Chi" string label at slot 2, which
    /// makes <see cref="DispatchCallOption"/> misroute it through the opcode-11
    /// button-row path — that's why three [11,0] dispatches silently no-opped
    /// and the bot froze. Routes around the label heuristic by firing opcode
    /// 12 directly.
    /// </summary>
    public unsafe DispatchResult DispatchChiVariant(int variantIndex)
    {
        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(12);
        values[1].SetInt(variantIndex);
        unit->FireCallback(2, values, true);
        return DispatchResult.Ok;
    }

    /// <summary>Dismisses the post-hand agari/draw result modal (state-29 "Next"). Routes through ReceiveEvent(ButtonClick) rather than FireCallback — the captured `[14]` was the addon's notification *after* the click, not the trigger; firing it directly landed the addon in stuck state-32 (2026-05-26).</summary>
    public unsafe DispatchResult DispatchHandResultNext()
    {
        const uint nextButtonNodeId = 97;
        const uint nextButtonCollisionId = 4;
        const int nextButtonEventParam = 7;

        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var btnNode = unit->GetNodeById(nextButtonNodeId);
        if (btnNode == null || (int)btnNode->Type < 1000)
            return DispatchResult.HookFailed;
        var compNode = (AtkComponentNode*)btnNode;
        if (compNode->Component == null)
            return DispatchResult.HookFailed;
        var collision = compNode->Component->UldManager.SearchNodeById(nextButtonCollisionId);
        if (collision == null)
            return DispatchResult.HookFailed;

        var atkEvent = new AtkEvent
        {
            Listener = (AtkEventListener*)compNode->Component,
            Node = collision,
            Target = (AtkEventTarget*)collision,
        };
        unit->ReceiveEvent(AtkEventType.ButtonClick, nextButtonEventParam, &atkEvent);
        return DispatchResult.Ok;
    }
}
