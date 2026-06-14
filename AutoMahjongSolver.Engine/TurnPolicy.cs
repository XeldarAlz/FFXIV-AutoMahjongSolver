namespace AutoMahjongSolver.Engine;

/// <summary>Everything the turn policy needs to pick a discard and decide on riichi.</summary>
public sealed record TurnInput
{
    public required int[] Counts14 { get; init; }
    public IReadOnlyList<Meld> Melds { get; init; } = Array.Empty<Meld>();
    public int[]? Seen { get; init; }
    public required EvalContext Context { get; init; }

    /// <summary>Rivers of opponents currently in riichi (their genbutsu).</summary>
    public IReadOnlyList<IReadOnlyList<int>> ThreatRivers { get; init; } = Array.Empty<IReadOnlyList<int>>();

    public IReadOnlyList<int> OwnRiver { get; init; } = Array.Empty<int>();

    /// <summary>Whether riichi can legally be declared this turn (button offered / wall and points allow).</summary>
    public bool RiichiAvailable { get; init; } = true;
}

public readonly record struct TurnDecision(int Discard, bool DeclareRiichi, string Reason);

/// <summary>
/// The discard-turn policy shared by the live bot and the offline simulator: value-ranked discards,
/// fold-to-safest against a riichi while still 1+ shanten, furiten-avoiding tenpai choice, and
/// riichi on any live closed tenpai (riichi is the win condition for yakuless hands and adds han +
/// ura pressure to everything else).
/// </summary>
public static class TurnPolicy
{
    public static TurnDecision Decide(TurnInput input)
    {
        List<ScoredDiscard> ranked = DiscardEvaluator.Rank(input.Counts14, input.Melds, input.Seen, input.Context);
        if (ranked.Count == 0)
            return new TurnDecision(-1, false, "no legal discard");

        DiscardOption best = ranked[0].Option;

        if (input.ThreatRivers.Count > 0 && best.Shanten >= 1)
        {
            int[] seen = input.Seen ?? input.Counts14;
            DiscardOption safest = best;
            int safestDanger = int.MaxValue;
            foreach (ScoredDiscard scored in ranked)
            {
                int danger = DangerCalculator.Danger(scored.Option.Tile, input.ThreatRivers, seen);
                if (danger < safestDanger)
                {
                    safestDanger = danger;
                    safest = scored.Option;
                }
            }

            return new TurnDecision(safest.Tile, false, $"fold vs riichi (danger {safestDanger})");
        }

        if (best.Shanten == 0 && IsFuriten(best, input.OwnRiver))
        {
            foreach (ScoredDiscard scored in ranked)
            {
                if (scored.Option.Shanten != 0)
                    break;
                if (!IsFuriten(scored.Option, input.OwnRiver))
                {
                    best = scored.Option;
                    return Finish(best, input, $"furiten dodge (cut {Tiles.Name(best.Tile)})");
                }
            }
        }

        return Finish(best, input, "best value");
    }

    private static TurnDecision Finish(in DiscardOption choice, TurnInput input, string reason)
    {
        bool riichi = input.RiichiAvailable
                      && input.Context.Closed
                      && choice.Shanten == 0
                      && choice.UkeireCount > 0;
        return new TurnDecision(choice.Tile, riichi, riichi ? $"{reason} + riichi" : reason);
    }

    private static bool IsFuriten(in DiscardOption option, IReadOnlyList<int> ownRiver)
    {
        foreach (AcceptTile accept in option.Accepts)
            if (ownRiver.Contains(accept.Tile))
                return true;
        return false;
    }
}
