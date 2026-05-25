using System.IO;
using Mahjong.Policy.Tuning;

namespace Mahjong.Policy.Tests;

public class JsonWeightProviderTests
{
    [Fact]
    public void Load_returns_default_bundle_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid()}.json");
        var provider = JsonWeightProvider.Load(path);
        Assert.Equal(WeightBundle.Default, provider.Current);
    }

    [Fact]
    public void Save_then_Load_round_trips_a_custom_bundle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"weights-{Guid.NewGuid()}.json");
        try
        {
            var custom = new DiscardWeights(
                Shanten: 999.0, UkeireKinds: 0.42, UkeireWeighted: 0.84,
                Dora: 12.5, Yakuhai: 33.0, IsolatedTerminal: 7.7, DealInCost: 0.001234);
            var bundle = WeightBundle.Default with { Discard = custom };

            JsonWeightProvider.Save(path, bundle);
            var loaded = JsonWeightProvider.Load(path).Current;

            Assert.Equal(custom, loaded.Discard);
            Assert.Equal(WeightBundle.CurrentSchemaVersion, loaded.SchemaVersion);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Load_throws_on_schema_version_mismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"weights-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "discard": { "shanten": 100, "ukeireKinds": 0, "ukeireWeighted": 0,
                               "dora": 0, "yakuhai": 0, "isolatedTerminal": 0, "dealInCost": 0 },
                  "opponent": { "tenpaiIntercept": 0, "tenpaiDiscardCount": 0,
                                "tenpaiMeldCount": 0, "tenpaiTurnsElapsed": 0,
                                "expectedHandValue": 0, "sujiDiscount": 0, "tenpaiBaseDealInRate": 0 },
                  "placement": { "rank1": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank1LastHand": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank1HugeLead": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank2Or3": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank2Or3LastHand": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank4": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank4LastHand": {"danger":1,"ukeire":1,"handValue":1},
                                 "rank1HugeLeadGap": 8000 },
                  "rollout": { "shantenPenalty": -100, "ukeireBonus": 1 },
                  "schemaVersion": 99
                }
                """);

            Assert.Throws<InvalidDataException>(() => JsonWeightProvider.Load(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
