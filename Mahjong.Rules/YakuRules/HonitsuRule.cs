namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Honitsu (3 han closed / 2 han open). One suit + honors.
/// Superseded by Chinitsu (which is one suit, no honors).
/// </summary>
public sealed class HonitsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Honitsu,
        Name: "Honitsu",
        ClosedHan: 3,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        if (d.Form == DecompositionForm.Kokushi)
            return [];

        int? singleSuit = null;
        bool hasHonor = false;
        foreach (var g in d.Groups)
        {
            var anchor = g.First;
            if (anchor.IsHonor)
            {
                hasHonor = true;
                continue;
            }
            int suit = (int)anchor.Suit;
            if (singleSuit is null)
                singleSuit = suit;
            else if (singleSuit != suit)
                return [];
        }

        return singleSuit is not null && hasHonor
            ? [new YakuHit(Definition.Id, Definition.Han(d.IsMenzen))]
            : [];
    }
}
