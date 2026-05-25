using Mahjong.Rules.Scoring;
using Mahjong.Rules.YakuRules;
using Mahjong.Rules.YakuRules.Yakuman;

namespace Mahjong.Rules.Rulesets;

public sealed class RiichiRuleSet : IRuleSet
{
    public string Name => "Riichi";

    public IReadOnlyList<IYakuRule> YakuRules { get; } =
    [
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
        new RyanpeikouRule(),
        new JunchanRule(),
        new HonitsuRule(),
        new ChinitsuRule(),
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

    public bool AllowsRedDora => false;
    public bool AllowsKuitan => true;
    public int MinHan => 1;
    public int KazoeThreshold => ScoringConstants.KazoeYakumanHan;
    public int MaxYakuman => 2;
}
