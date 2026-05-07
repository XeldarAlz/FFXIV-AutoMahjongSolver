namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Suuankou (yakuman). Four concealed triplets (concealed kans count).
///
/// TODO (docs/ruleset.md Q7): Suuankou-tanki — winning on a single-tile wait
/// where the pair completes the hand — is a double yakuman in many variants.
/// Not currently distinguished; would be added as <c>SuuankouTankiRule</c> with
/// Conflicts = [Suuankou].
/// </summary>
public sealed class SuuankouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Suuankou,
        Name: "Suuankou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form != DecompositionForm.Standard)
            return [];

        int ankou = d.ConcealedTripletCount + CountConcealedKans(d);
        return ankou == 4
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
    }

    private static int CountConcealedKans(Decomposition d)
    {
        int count = 0;
        foreach (var g in d.Groups)
        {
            if (g.IsConcealedKan)
                count++;
        }
        return count;
    }
}
