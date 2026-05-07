using Mahjong.Rules.Scoring;
using Mahjong.Rules.YakuRules;
using Mahjong.Rules.YakuRules.Yakuman;

namespace Mahjong.Rules.Rulesets;

/// <summary>
/// Standard Japanese riichi rule set. Drives Tenhou replay parsing in
/// <c>Mahjong.Replay</c> — Tenhou games are riichi-rules, so any policy
/// trained on Tenhou logs must score them under this set.
///
/// All 38 yaku are registered, ordered by han value for readability — the
/// Scorer iterates the entire list and applies conflicts after detection,
/// so registration order doesn't affect correctness.
/// </summary>
public sealed class RiichiRuleSet : IRuleSet
{
    public string Name => "Riichi";

    public IReadOnlyList<IYakuRule> YakuRules { get; } =
    [
        // ---- 1 han ----
        new RiichiRule(),
        new IppatsuRule(),
        new MenzenTsumoRule(),
        new PinfuRule(),
        new TanyaoRule(),
        new IipeikoRule(),
        new YakuhaiRule(),
        new RinshanRule(),
        new ChankanRule(),
        new HaiteiRule(),
        new HouteiRule(),

        // ---- 2 han ----
        new DoubleRiichiRule(),
        new ChiitoitsuRule(),
        new SanshokuDoujunRule(),
        new SanshokuDoukouRule(),
        new IttsuRule(),
        new ToitoiRule(),
        new SanankouRule(),
        new SankantsuRule(),
        new HonroutouRule(),
        new ShousangenRule(),
        new ChantaRule(),

        // ---- 3 han ----
        new RyanpeikouRule(),
        new JunchanRule(),
        new HonitsuRule(),

        // ---- 6 han ----
        new ChinitsuRule(),

        // ---- yakuman ----
        new KokushiRule(),
        new SuuankouRule(),
        new DaisangenRule(),
        new ShousuushiiRule(),
        new DaisuushiiRule(),
        new TsuuiisouRule(),
        new ChinroutouRule(),
        new RyuuiisouRule(),
        new ChuurenPoutouRule(),
        new SuukantsuRule(),
        new TenhouRule(),
        new ChihouRule(),
    ];

    public IScoringRule ScoringRule { get; } = new StandardScoringRule();
    public IDoraRule DoraRule { get; } = new StandardDoraRule();
    public IFuRule FuRule { get; } = new StandardFuRule();

    public bool AllowsRedDora => false;       // Tenhou logs we replay don't carry red dora
    public bool AllowsKuitan => true;          // riichi default — open tanyao counts
    public int MinHan => 1;
    public int KazoeThreshold => ScoringConstants.KazoeYakumanHan;
    public int MaxYakuman => 2;                // double yakuman cap (Daisuushii, pure Chuuren)
}
