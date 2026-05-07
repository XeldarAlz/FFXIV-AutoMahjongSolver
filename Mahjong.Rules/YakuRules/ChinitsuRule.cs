namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Chinitsu (6 han closed / 5 han open). Single suit, no honors.
/// Supersedes Honitsu.
/// </summary>
public sealed class ChinitsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chinitsu,
        Name: "Chinitsu",
        ClosedHan: 6,
        OpenHan: 5);

    public IReadOnlyList<Mahjong.Core.Yaku> Conflicts { get; } = [Mahjong.Core.Yaku.Honitsu];

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form == DecompositionForm.Kokushi)
            return [];

        int? singleSuit = null;
        foreach (var g in d.Groups)
        {
            var anchor = g.First;
            if (anchor.IsHonor)
                return [];
            int suit = (int)anchor.Suit;
            if (singleSuit is null)
                singleSuit = suit;
            else if (singleSuit != suit)
                return [];
        }

        return singleSuit is not null
            ? [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))]
            : [];
    }
}
