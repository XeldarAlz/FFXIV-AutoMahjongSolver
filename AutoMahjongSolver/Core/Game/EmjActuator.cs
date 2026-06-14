using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoMahjongSolver.Core.Game;

/// <summary>
/// Actuates Doman Mahjong inputs by replaying the FireCallback protocol captured from manual play.
/// A discard is a two-call handshake: [15, iconId] selects/highlights the tile (and moves the
/// after-draw surface from state 6 to state 30), then [7, slot] commits it. Gated on the turn state
/// so a mis-timed call cannot wedge the addon.
/// </summary>
internal static unsafe class EmjActuator
{
    private const int OpHighlight = 15;
    private const int OpDiscardCommit = 7;
    private const int OpCallOption = 11;

    private const int StateAfterDraw = 6;       // self-action surface (riichi/tsumo/kan or discard)
    private const int StateAfterCall = 28;      // post-call discard surface (verified live after a pon)
    private const int StateDiscardTurn = 30;    // tile-selection discard surface

    /// <summary>Discard the given hand slot (0-12 hand position, 13 = drawn tile).</summary>
    public static bool DiscardSlot(int slot)
    {
        if (slot is < 0 or > 13)
        {
            AmjsLog.Warn($"discard: slot {slot} out of range.");
            return false;
        }

        if (!EmjBoardReader.TryReadHand(out HandSnapshot snap) || snap.SlotTiles[slot] < 0)
        {
            AmjsLog.Warn($"discard: slot {slot} is empty or hand unreadable.");
            return false;
        }

        return Commit(slot, snap.SlotIcons[slot]);
    }

    /// <summary>
    /// Find the given tile in the current hand and discard it, preferring a plain copy over a red
    /// five so the aka's extra han stays in the hand whenever possible.
    /// </summary>
    public static bool DiscardTile(int tile)
    {
        if (!EmjBoardReader.TryReadHand(out HandSnapshot snap))
        {
            AmjsLog.Warn("discard: could not read hand.");
            return false;
        }

        int akaSlot = -1;
        for (int slot = 0; slot < snap.SlotTiles.Length; slot++)
        {
            if (snap.SlotTiles[slot] != tile)
                continue;
            if (!EmjTiles.IsAkaIcon(snap.SlotIcons[slot]))
                return Commit(slot, snap.SlotIcons[slot]);
            akaSlot = slot;
        }

        if (akaSlot >= 0)
            return Commit(akaSlot, snap.SlotIcons[akaSlot]);

        AmjsLog.Warn($"discard: tile {Engine.Tiles.Name(tile)} not in hand.");
        return false;
    }

    private static bool Commit(int slot, int iconId)
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("discard: Emj addon not present.");
            return false;
        }

        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
        if (state != StateDiscardTurn && state != StateAfterDraw && state != StateAfterCall)
        {
            AmjsLog.Warn($"discard: not our turn (state={state}, need {StateAfterDraw}, {StateAfterCall} or {StateDiscardTurn}).");
            return false;
        }

        if (iconId > 0)
            FireInts(addon, OpHighlight, iconId);
        FireInts(addon, OpDiscardCommit, slot);

        AmjsLog.Info($"discard: fired [15, {iconId}] + [7, {slot}] (state {state}).");
        return true;
    }

    /// <summary>
    /// Pick a call / self-action option ([11, option]) in a call (15) or self-action (6/30) popup.
    /// State 30 carries the Riichi/Pass prompt (captured live: manual riichi fired [11, 0] at state 30).
    /// </summary>
    public static bool Call(int option)
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("call: Emj addon not present.");
            return false;
        }

        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
        if (state is not (6 or 15 or 30))
        {
            AmjsLog.Warn($"call: not a call/self-action state (state={state}).");
            return false;
        }

        FireInts(addon, OpCallOption, option);
        AmjsLog.Info($"call: fired [11, {option}] (state {state}).");
        return true;
    }

    /// <summary>Fire the captured advance opcode for the current result / new-hand state (test only).</summary>
    public static bool Advance()
    {
        if (!EmjBoardReader.TryGetAddon(out AtkUnitBase* addon))
        {
            AmjsLog.Warn("advance: Emj addon not present.");
            return false;
        }

        int state = addon->AtkValues != null ? addon->AtkValues[0].Int : -1;
        int op = state switch
        {
            29 => 14, // agari / result modal
            32 => 17, // end-of-hand transitional
            2 => 9,   // new-hand acknowledge
            _ => -1,
        };

        if (op < 0)
        {
            AmjsLog.Warn($"advance: nothing to advance (state={state}).");
            return false;
        }

        FireOne(addon, op);
        AmjsLog.Info($"advance: fired [{op}] (state {state}).");
        return true;
    }

    private static void FireInts(AtkUnitBase* addon, int op, int arg)
    {
        AtkValue* values = stackalloc AtkValue[2];
        values[0].SetInt(op);
        values[1].SetInt(arg);
        addon->FireCallback(2, values, true);
    }

    private static void FireOne(AtkUnitBase* addon, int op)
    {
        AtkValue* values = stackalloc AtkValue[1];
        values[0].SetInt(op);
        addon->FireCallback(1, values, true);
    }
}
