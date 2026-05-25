namespace Mahjong.Rules.YakuRules;

public sealed class ChiitoitsuRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Chiitoitsu,
        Name: "Chiitoitsu",
        ClosedHan: 2,
        OpenHan: 0,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => d.Form == DecompositionForm.Chiitoitsu
            ? [new YakuHit(Definition.Id, Definition.ClosedHan)]
            : [];
}
