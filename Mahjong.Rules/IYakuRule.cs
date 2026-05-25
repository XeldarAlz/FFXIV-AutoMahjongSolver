namespace Mahjong.Rules;

public interface IYakuRule
{
    YakuDefinition Definition { get; }

    IReadOnlyList<YakuHit> Detect(Decomposition d, WinContext ctx);

    /// <summary>Yaku superseded when this fires (Ryanpeikou→Iipeiko, Chinitsu→Honitsu, etc.).</summary>
    IReadOnlyList<Yaku> Conflicts => [];
}

/// <summary><see cref="OpenHan"/>=0 means closed-only.</summary>
public sealed record YakuDefinition(
    Yaku Id,
    string Name,
    int ClosedHan,
    int OpenHan,
    bool IsYakuman = false,
    bool RequiresMenzen = false)
{
    public int Han(bool isMenzen) => isMenzen ? ClosedHan : OpenHan;
}
