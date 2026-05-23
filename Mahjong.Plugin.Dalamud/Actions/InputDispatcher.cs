using System;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Plugin.Dalamud.GameState;

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
    private readonly MahjongAddon addon;

    public InputDispatcher(MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        this.addon = addon;
    }

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
    /// <para>Three click paths cover the addon's discard UI shapes:</para>
    /// <list type="bullet">
    ///   <item><b>List-widget click</b> (<see cref="TryDispatchListItemClick"/>):
    ///     state-6 SelfDeclareList post-call popup (hand.Count != 14) and
    ///     state-28 CallPromptList novice-table popup. The call modal at
    ///     node 104 is visible AND its inner shell at node 3 is an
    ///     AtkComponentList; SelectItem on the slot index commits.</item>
    ///   <item><b>Opcode 15 tile-click</b>: state-6 SelfDeclareList with no
    ///     call modal visible — the addon shows the closed hand as the
    ///     primary clickable surface and expects a tile-texture click, not
    ///     a slot-index callback. Corpus evidence (2026-05-18 inputs
    ///     telemetry): 226 game-source FireCallback([15, textureBase +
    ///     tile_id]) records from manual user discards, confirming this is
    ///     the path the game receives when a player clicks a hand tile.
    ///     Opcode 7 silently no-ops here (verified 2026-05-19: stuck
    ///     session with dispatch_attempted result=Ok repeating every 3 s
    ///     while game state stayed at state=6 hand=14).</item>
    ///   <item><b>Opcode 7 slot-discard</b>: state-30 OurTurnDiscard — the
    ///     classic in-hand discard surface where the slot index is what
    ///     the addon's callback expects.</item>
    /// </list>
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

        // State-6 SelfDeclareList: opcode 15 (tile-texture click) is what
        // manual user discards fire on the live addon — 226 corpus records
        // across the 2026-05-18 inputs stream confirm the shape
        // [15, textureBase + tile_id]. This path is the priority at state=6
        // because the addon's main shell at this state IS a list widget
        // (node 104 visible, node 3 type 1030), so IsListWidgetPopupActive
        // would otherwise short-circuit to SelectItem, which silently no-ops
        // when the list is the closed-hand-as-discard-surface rather than a
        // dedicated post-call popup. v0.1.0.8 added the opcode-15 path but
        // gated it AFTER the list-widget check, so it was unreachable —
        // user reproduced the stall pattern with v0.1.0.8 on 2026-05-23
        // (state=6 hand=14 flags=Discard, dispatch result=Ok every 3s, game
        // state never advancing).
        if (ReadStateCode(unit) == StateCodeSelfDeclareList)
        {
            int raw = ReadHandSlotRaw(unit, slotIndex);
            if (raw > 0)
            {
                var v15 = stackalloc AtkValue[2];
                v15[0].SetInt(15);
                v15[1].SetInt(raw);
                unit->FireCallback(2, v15, true);
                LastDiscardPath = $"opcode-15(raw={raw})";
                return DispatchResult.Ok;
            }
        }

        // Non-state-6 list-widget popup paths (state-28 chi list, etc.).
        // The list items here ARE the discardable surface and SelectItem
        // commits cleanly (verified across post-chi/pon discard popups
        // 2026-05-10..05-18).
        if (IsListWidgetPopupActive(unit) && TryDispatchListItemClick(unit, slotIndex))
        {
            LastDiscardPath = $"list-widget(slot={slotIndex})";
            return DispatchResult.Ok;
        }

        // Classic state-30 in-hand discard: slot-index callback opcode 7.
        var values = stackalloc AtkValue[2];
        values[0].SetInt(7);
        values[1].SetInt(slotIndex);
        bool ok = unit->FireCallback(2, values, true);
        LastDiscardPath = $"opcode-7(slot={slotIndex},ok={ok})";
        return ok ? DispatchResult.Ok : DispatchResult.HookFailed;
    }

    // SelfDeclareList state code from the Emj/EmjL layout profiles. Hardcoded
    // here rather than threaded through from LayoutProfile because the
    // dispatcher's only state-aware branch needs the value at exactly one
    // point and the value has been stable across both shipping variants
    // (data/layouts/{emj,emj_l}.json both set selfDeclareList=6).
    private const int StateCodeSelfDeclareList = 6;

    // Hand-array byte offset, identical across Emj and EmjL (verified
    // 2026-05-18). Each tile is a 4-byte texture-relative int.
    private const int HandArrayStartOffset = 0x0DB8;

    private static unsafe int ReadStateCode(AtkUnitBase* unit)
    {
        if (unit->AtkValues == null || unit->AtkValuesCount == 0)
            return -1;
        var v = unit->AtkValues[0];
        return v.Type == FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType.Int ? v.Int : -1;
    }

    private static unsafe int ReadHandSlotRaw(AtkUnitBase* unit, int slotIndex)
    {
        if (slotIndex is < 0 or > 13)
            return 0;
        byte* basePtr = (byte*)unit;
        return *(int*)(basePtr + HandArrayStartOffset + slotIndex * 4);
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

    /// <summary>
    /// Find the slot index (0..13) of a given tile in the hand. Returns -1 if not found.
    /// For duplicate tiles, prefers the last-drawn slot (13) if the tile matches there,
    /// otherwise the lowest sorted slot.
    /// </summary>
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
    /// Opcode constants for FireCallback's first AtkValue. Discard = 7,
    /// CallPrompt = 11, and Tsumo = 9 are confirmed from corpus analysis:
    /// the inputs telemetry shows 16 <c>[9]</c> records across 14 distinct
    /// installs over 2026-05-10..05-18, all with count=1 matching this
    /// dispatcher's signature. Riichi / Ron / Kan are still speculative —
    /// the corpus has zero observations of opcodes 8 / 10 / 12.
    /// </summary>
    private static class Opcode
    {
        public const int Discard = 7;
        public const int CallPrompt = 11;
        public const int Tsumo = 9;     // confirmed: 14 installs, 16 corpus records

        // Speculative — to be confirmed by in-game FireCallback capture:
        public const int Riichi = 8;    // unconfirmed
        public const int Ron = 10;      // unconfirmed
        public const int Kan = 12;      // unconfirmed (shouminkan + ankan from our turn)
    }

    /// <summary>
    /// Declare riichi while also discarding the tile at <paramref name="slotIndex"/>.
    ///
    /// <para><b>WARNING:</b> opcode 8 is speculative AND unused in the live flow.
    /// Corpus cross-reference (tools/cross-ref-action-opcodes.mjs) of 8
    /// action=riichi events against the inputs stream finds zero opcode-8
    /// FireCallbacks; instead 4 of 8 riichi events pair with opcode 11
    /// (CallPrompt) within ±2 s. The production sequence is:</para>
    /// <list type="number">
    ///   <item>State-6 hand=14 popup: <see cref="DispatchCallOption"/> with the
    ///         Riichi button index (opcode 11).</item>
    ///   <item><see cref="ActionStateMachine.LatchRiichiConfirm"/> latches
    ///         post-click.</item>
    ///   <item>Next tick: <see cref="AutoPlayLoop.ScheduleRiichiTsumogiri"/>
    ///         calls <see cref="DispatchDiscard"/> (opcode 7) on the drawn tile.</item>
    /// </list>
    /// <para>This combined Riichi+discard dispatch is therefore dead code on the
    /// shipping Doman addon. Retained as forward-compat scaffolding in case a
    /// future variant exposes a true single-callback opcode.</para>
    /// </summary>
    public unsafe DispatchResult DispatchRiichi(int slotIndex)
    {
        if (slotIndex is < 0 or > 13)
            return DispatchResult.InvalidSlot;

        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(Opcode.Riichi);
        values[1].SetInt(slotIndex);
        bool ok = unit->FireCallback(2, values, true);
        return ok ? DispatchResult.Ok : DispatchResult.HookFailed;
    }

    /// <summary>
    /// Declare tsumo on the last-drawn tile. Opcode 9 is confirmed via corpus
    /// (14 installs, 16 records across 2026-05-10..05-18). FireCallback
    /// returns <c>false</c> for this opcode the same way it does for the
    /// call-prompt opcode 11 — visibly accepted in-game even when the return
    /// indicates failure. Always reports <see cref="DispatchResult.Ok"/>; the
    /// caller is expected to have verified the modal-visibility / legal-action
    /// gate before dispatching.
    /// </summary>
    public unsafe DispatchResult DispatchTsumo()
    {
        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var values = stackalloc AtkValue[1];
        values[0].SetInt(Opcode.Tsumo);
        unit->FireCallback(1, values, true);
        return DispatchResult.Ok;
    }

    /// <summary>
    /// Declare ron on the last opponent discard. WARNING: opcode unconfirmed. Ron may
    /// actually be offered as a call prompt (opcode 11) with a distinct option index;
    /// if so, <see cref="DispatchCallOption"/> already handles it and this stub
    /// is not needed.
    /// </summary>
    public unsafe DispatchResult DispatchRon()
    {
        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var values = stackalloc AtkValue[1];
        values[0].SetInt(Opcode.Ron);
        bool ok = unit->FireCallback(1, values, true);
        return ok ? DispatchResult.Ok : DispatchResult.HookFailed;
    }

    /// <summary>
    /// Declare kan from our own turn (ankan or shouminkan). WARNING: opcode unconfirmed.
    /// </summary>
    public unsafe DispatchResult DispatchKan(int slotIndex)
    {
        if (slotIndex is < 0 or > 13)
            return DispatchResult.InvalidSlot;

        if (!addon.TryGet(out var unit, out _))
            return DispatchResult.AddonNotFound;
        if (!unit->IsVisible)
            return DispatchResult.AddonNotVisible;

        var values = stackalloc AtkValue[2];
        values[0].SetInt(Opcode.Kan);
        values[1].SetInt(slotIndex);
        bool ok = unit->FireCallback(2, values, true);
        return ok ? DispatchResult.Ok : DispatchResult.HookFailed;
    }
}
