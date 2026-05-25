using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mahjong.Core;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using Mahjong.Plugin.Dalamud.Tests.Stubs;
using Mahjong.Plugin.Game.Variants;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

/// <summary>Drives BuildSnapshotFromMemory against every fixture under Replay/fixtures/*.json. Add a new file → CI gates on it automatically.</summary>
public class ReplayFixtureTests
{
    public static IEnumerable<object[]> AllFixtures()
    {
        if (!Directory.Exists(TestPaths.FixturesDir))
            yield break;
        foreach (var (path, _) in FixtureLoader.LoadAll(TestPaths.FixturesDir))
            yield return new object[] { Path.GetFileNameWithoutExtension(path) };
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Fixture_matches_expected_snapshot(string fixtureName)
    {
        var path = Path.Combine(TestPaths.FixturesDir, fixtureName + ".json");
        var fixture = FixtureLoader.Load(path);

        var profile = JsonLayoutProfileLoader.Load(
            Path.Combine(TestPaths.LayoutsDir, ResolveLayoutFileName(fixture.Variant)));

        byte[] memory = Convert.FromBase64String(fixture.AddonMemoryBase64);
        if (memory.Length < AddonMemoryBuilder.BufferSize)
        {
            var padded = new byte[AddonMemoryBuilder.BufferSize];
            Buffer.BlockCopy(memory, 0, padded, 0, memory.Length);
            memory = padded;
        }

        var atkValues = fixture.AtkValues.Select(MarshalAtkValue).ToArray();

        var variant = new BaseEmjVariant(profile, new StubPluginLog(), Path.GetTempPath());
        var ctx = new VariantReadContext(new MeldTracker(), EventLogger: null);

        var snap = variant.BuildSnapshotFromMemory(
            memory, atkValues, ctx,
            fixture.CallModalVisible,
            fixture.ListWidgetLabels);

        Assert.NotNull(snap);

        var exp = fixture.Expected;
        if (exp.StateCode is int sc)
            Assert.Equal(sc, snap!.AddonStateCode);
        if (exp.Hand is string handExpr)
            AssertHandEquals(handExpr, snap!.Hand);
        if (exp.LegalFlags is { Count: > 0 } flagNames)
            Assert.Equal(ParseFlags(flagNames), snap!.Legal.Flags);
        if (exp.ScoreSelf is int score)
            Assert.Equal(score, snap!.Scores[0]);
        if (exp.WallRemaining is int wall)
            Assert.Equal(wall, snap!.WallRemaining);
        if (exp.AkaDora is int aka)
            Assert.Equal(aka, snap!.AkaDora);
        if (exp.MeldCount is int meldCount)
            Assert.Equal(meldCount, snap!.OurMelds.Count);
    }

    private static AtkValueRecord MarshalAtkValue(ReplayAtkValue v)
    {
        var type = Enum.Parse<ValueType>(v.Type, ignoreCase: true);
        return type switch
        {
            ValueType.Int => AtkValueRecord.OfInt(v.Int ?? 0),
            ValueType.UInt => new AtkValueRecord(type, 0, v.UInt ?? 0u, null),
            ValueType.Bool => new AtkValueRecord(type, 0, (v.Bool ?? false) ? 1u : 0u, null),
            ValueType.String or ValueType.String8 or ValueType.ManagedString =>
                new AtkValueRecord(type, 0, 0, v.String),
            _ => AtkValueRecord.Empty,
        };
    }

    private static ActionFlags ParseFlags(IEnumerable<string> names)
    {
        ActionFlags flags = ActionFlags.None;
        foreach (var n in names)
            flags |= Enum.Parse<ActionFlags>(n, ignoreCase: true);
        return flags;
    }

    private static void AssertHandEquals(string expr, IReadOnlyList<Tile> actual)
    {
        var expected = Tiles.Parse(expr);
        var expectedSorted = expected.Select(t => t.Id).OrderBy(id => id).ToArray();
        var actualSorted = actual.Select(t => t.Id).OrderBy(id => id).ToArray();
        Assert.Equal(expectedSorted, actualSorted);
    }

    private static string ResolveLayoutFileName(string variant) => variant switch
    {
        "Emj" => "emj.json",
        "EmjL" => "emj_l.json",
        _ => throw new InvalidDataException($"Unknown variant '{variant}' — add a mapping in {nameof(ResolveLayoutFileName)}"),
    };

    /// <summary>Regenerates the synthetic seed fixtures in the source tree. Opt-in via MJ_REGEN_FIXTURES=1 — keep idle in normal CI so source-tree writes don't surprise contributors.</summary>
    [Fact]
    public void Regenerate_synthetic_seed_fixtures()
    {
        if (Environment.GetEnvironmentVariable("MJ_REGEN_FIXTURES") != "1")
            return;

        var emj = JsonLayoutProfileLoader.Load(Path.Combine(TestPaths.LayoutsDir, "emj.json"));
        var seedDir = Path.Combine(TestPaths.RepoRoot,
            "tests", "Mahjong.Plugin.Dalamud.Tests", "Replay", "fixtures");
        Directory.CreateDirectory(seedDir);

        WriteFixture(Path.Combine(seedDir, "state30_our_turn_emj.json"), new ReplayFixture
        {
            Name = "state30_our_turn_emj",
            Description = "State-30 our-turn-discard on Emj. Fresh 14-tile hand, all scores 25000, no calls in flight.",
            Variant = "Emj",
            AddonMemoryBase64 = Convert.ToBase64String(
                new AddonMemoryBuilder(emj)
                    .WithScores(25000, 25000, 25000, 25000)
                    .WithDiscardCounts(0, 0, 0, 0)
                    .WithHand("1234m456p789s1234z")
                    .WithDoraIndicator("5z")
                    .Build()),
            AtkValues = new List<ReplayAtkValue>
            {
                new() { Type = "Int", Int = 30 },
                new() { Type = "Int", Int = 0 },
            },
            CallModalVisible = false,
            Expected = new ReplayExpected
            {
                StateCode = 30,
                Hand = "1234m456p789s1234z",
                LegalFlags = new List<string> { "Discard" },
                ScoreSelf = 25000,
                WallRemaining = 70,
                AkaDora = 0,
                MeldCount = 0,
            },
        });

        // State-15 call prompt offering Pon on 5m. Hand has a 55m pair; atkValues carry the "Pon" + "Pass" labels in the button-label scan window and the claimed-tile (raw=textureBase+4) duplicated in [16..17] so AppendPonCandidateFromAtkValues picks it up.
        var pon5mClaimedRaw = emj.TileTextureBase + 4;
        var ponAtkValues = new List<ReplayAtkValue>(22);
        for (int i = 0; i < 22; i++) ponAtkValues.Add(new ReplayAtkValue { Type = "Undefined" });
        ponAtkValues[emj.AtkValues.StateCode] = new ReplayAtkValue { Type = "Int", Int = 15 };
        ponAtkValues[2] = new ReplayAtkValue { Type = "String", String = "Pon" };
        ponAtkValues[3] = new ReplayAtkValue { Type = "String", String = "Pass" };
        ponAtkValues[16] = new ReplayAtkValue { Type = "Int", Int = pon5mClaimedRaw };
        ponAtkValues[17] = new ReplayAtkValue { Type = "Int", Int = pon5mClaimedRaw };

        WriteFixture(Path.Combine(seedDir, "state15_pon_offer_emj.json"), new ReplayFixture
        {
            Name = "state15_pon_offer_emj",
            Description = "State-15 call prompt offering Pon on 5m. Hand carries the 55m pair; modal visible.",
            Variant = "Emj",
            AddonMemoryBase64 = Convert.ToBase64String(
                new AddonMemoryBuilder(emj)
                    .WithScores(25000, 25000, 25000, 25000)
                    .WithDiscardCounts(0, 0, 1, 0)  // opp (toimen) just discarded the 5m
                    .WithHand("55m123p456s11234z")  // 13 tiles incl. 55m pair
                    .WithDoraIndicator("1z")
                    .Build()),
            AtkValues = ponAtkValues,
            CallModalVisible = true,
            Expected = new ReplayExpected
            {
                StateCode = 15,
                LegalFlags = new List<string> { "Pon", "Pass" },
                ScoreSelf = 25000,
                WallRemaining = 69,  // 70 - 1 opp discard
                AkaDora = 0,
                MeldCount = 0,
            },
        });
    }

    private static void WriteFixture(string path, ReplayFixture fixture)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(fixture, opts));
    }
}
