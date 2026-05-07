namespace Mahjong.Rules.YakuRules.Yakuman;

/// <summary>
/// Tenhou (yakuman). The dealer wins on the very first tsumo of the hand,
/// before any call has occurred.
/// </summary>
public sealed class TenhouRule : IYakuRule
{
    public YakuDefinition Definition { get; } = new(
        Id: Mahjong.Core.Yaku.Tenhou,
        Name: "Tenhou",
        ClosedHan: HanValues.Yakuman,
        OpenHan: HanValues.Yakuman,
        IsYakuman: true,
        RequiresMenzen: true);

    public IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx)
        => ctx.IsTenhou
            ? [new YakuHit(Definition.Id, HanValues.Yakuman, IsYakuman: true)]
            : [];
}
