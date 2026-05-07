namespace Mahjong.Rules.YakuRules;

/// <summary>
/// Honroutou (2 han). All terminals + honors. No simples, no runs anywhere.
/// </summary>
public sealed class HonroutouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Honroutou,
        Name: "Honroutou",
        ClosedHan: 2,
        OpenHan: 2);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
    {
        // Runs always contain a simple, so any run disqualifies.
        foreach (var g in d.Groups)
        {
            if (g.Kind == GroupKind.Run)
                return [];
            if (!g.First.IsTerminalOrHonor)
                return [];
        }
        return [new YakuHit(Definition.Id, 2)];
    }
}
