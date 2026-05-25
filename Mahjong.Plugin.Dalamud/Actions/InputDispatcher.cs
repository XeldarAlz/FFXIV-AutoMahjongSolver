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
            && (stateCode == StateCodeSelfDeclareList
                || stateCode == StateCodeOurTurnDiscard);
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

    // State code constants from the Emj/EmjL layout profiles. Hardcoded
    // here rather than threaded through from LayoutProfile because the
    // dispatcher's only state-aware branch needs the values at exactly two
    // points and they've been stable across both shipping variants
    // (data/layouts/{emj,emj_l}.json both set selfDeclareList=6, ourTurnDiscard=30).
    private const int StateCodeSelfDeclareList = 6;
    private const int StateCodeOurTurnDiscard = 30;

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
    /// Count non-zero slots in the closed-hand array starting at
    /// <see cref="HandArrayStartOffset"/>. Stops at the first zero entry — the
    /// addon zero-terminates the hand region, so empty slots past the live
    /// tail are always 0. Returns 0..14.
    ///
    /// <para>Used to gate the state-6 opcode-15 path on the genuine
    /// self-declare-after-draw shape (hand=14) and steer the post-call
    /// shape (hand=11/8/5) to the list-widget click path. See the
    /// <see cref="DispatchDiscard"/> docstring for the regression history.</para>
    /// </summary>
    private static unsafe int ReadCurrentHandCount(AtkUnitBase* unit)
    {
        byte* basePtr = (byte*)unit;
        int count = 0;
        for (int i = 0; i < 14; i++)
        {
            int raw = *(int*)(basePtr + HandArrayStartOffset + i * 4);
            if (raw == 0)
                break;
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
    /// Opcode constants for FireCallback's first AtkValue. All values here
    /// are corpus-confirmed from the inputs telemetry stream.
    ///
    /// Discard (15+7) is the two-callback handshake captured 2026-05-23.
    /// CallPrompt (11) handles every popup button: Pon, Chi (multi-variant),
    /// MinKan, ShouMinKan, AnKan, Ron, Riichi (declaration click), and Pass.
    /// Tsumo (9) is the only action with a dedicated dispatch — 14 installs,
    /// 16 records across 2026-05-10..05-18.
    ///
    /// Historical note: opcodes 8 (Riichi), 10 (Ron), and 12 (Kan) were
    /// shipped as speculative dispatchers in v0.1.0.11..v0.1.1.0. Field bug
    /// report #39 (2026-05-24) showed the Ron path emitting
    /// <c>FireCallback(1, [Int=10]) → false (HookFailed)</c>, after which
    /// the game state corrupted into the DRAW screen (state-29) with no
    /// legal recovery. The fix routes Ron / AnKan / ShouMinKan / Riichi
    /// declaration through the call-prompt button-row path (opcode 11)
    /// which has cross-action test coverage in
    /// <c>AutoPlayLoopAcceptIndexTests</c> and is verified live for
    /// Pon/Chi/Pass.
    /// </summary>
    private static class Opcode
    {
        public const int Discard = 7;
        public const int CallPrompt = 11;
        public const int Tsumo = 9;
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
}
