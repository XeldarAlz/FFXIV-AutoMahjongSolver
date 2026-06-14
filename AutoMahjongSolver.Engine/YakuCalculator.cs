namespace AutoMahjongSolver.Engine;

/// <summary>
/// Detects yaku and dora for a complete hand: concealed tiles (winning tile included) plus called
/// melds. Evaluates every standard decomposition, seven pairs, and kokushi, and returns the
/// highest-value interpretation. Returns null if the hand is not actually complete.
/// </summary>
public static class YakuCalculator
{
    public const int YakumanHan = 13;

    public static WinResult? Evaluate(ReadOnlySpan<int> concealed, IReadOnlyList<Meld>? melds, in WinContext ctx)
    {
        melds ??= Array.Empty<Meld>();
        if (ShantenCalculator.Calculate(concealed, melds.Count) != ShantenCalculator.Complete)
            return null;

        bool closed = true;
        foreach (Meld m in melds)
        {
            if (m.IsOpen)
            {
                closed = false;
                break;
            }
        }

        int[] total = concealed.ToArray();
        foreach (Meld m in melds)
            foreach (int t in m.Tiles)
                total[t]++;

        List<YakuHan>? best = null;
        int bestHan = -1;
        bool bestYakuman = false;

        if (melds.Count == 0 && ShantenCalculator.ThirteenOrphans(concealed) == ShantenCalculator.Complete)
        {
            best = new List<YakuHan> { new(Yaku.KokushiMusou, YakumanHan) };
            bestYakuman = true;
        }
        else
        {
            foreach (Decomposition d in HandDecomposer.Decompose(concealed))
                Consider(EvaluateStandard(d, melds, closed, total, ctx), ref best, ref bestHan, ref bestYakuman);

            if (melds.Count == 0 && ShantenCalculator.SevenPairs(concealed) == ShantenCalculator.Complete)
                Consider(EvaluateChiitoi(total), ref best, ref bestHan, ref bestYakuman);
        }

        if (best is null)
            return null;

        if (!bestYakuman)
            AddContextYaku(best, closed, ctx);

        int yakuHan = bestYakuman ? YakumanHan : SumHan(best);
        int doraHan = bestYakuman ? 0 : CountDora(total, ctx);

        return new WinResult
        {
            Yaku = best,
            YakuHanTotal = yakuHan,
            DoraHan = doraHan,
            IsYakuman = bestYakuman,
        };
    }

    /// <summary>The dora tile named by an indicator: next rank in suit (9 wraps to 1), E→S→W→N→E, Haku→Hatsu→Chun→Haku.</summary>
    public static int DoraFromIndicator(int indicator)
    {
        if (Tiles.IsNumber(indicator))
        {
            int suitBase = indicator / 9 * 9;
            return suitBase + (indicator - suitBase + 1) % 9;
        }

        if (indicator <= 30)
            return 27 + (indicator - 27 + 1) % 4;
        return 31 + (indicator - 31 + 1) % 3;
    }

    private static void Consider(
        (List<YakuHan> Yaku, bool Yakuman)? candidate, ref List<YakuHan>? best, ref int bestHan, ref bool bestYakuman)
    {
        if (candidate is not { } c)
            return;

        int han = c.Yakuman ? int.MaxValue : SumHan(c.Yaku);
        if (best is null || (c.Yakuman && !bestYakuman) || (c.Yakuman == bestYakuman && han > bestHan))
        {
            best = c.Yaku;
            bestHan = han;
            bestYakuman = c.Yakuman;
        }
    }

    private static int SumHan(List<YakuHan> yaku)
    {
        int sum = 0;
        foreach (YakuHan y in yaku)
            sum += y.Han;
        return sum;
    }

    private enum WaitShape
    {
        Ryanmen,
        Kanchan,
        Penchan,
        Shanpon,
        Tanki,
    }

    private static (List<YakuHan> Yaku, bool Yakuman)? EvaluateStandard(
        Decomposition d, IReadOnlyList<Meld> melds, bool closed, int[] total, in WinContext ctx)
    {
        var blocks = new SetBlock[d.Sets.Count + melds.Count];
        for (int i = 0; i < d.Sets.Count; i++)
            blocks[i] = d.Sets[i];
        for (int i = 0; i < melds.Count; i++)
        {
            Meld m = melds[i];
            blocks[d.Sets.Count + i] = m.Kind switch
            {
                MeldKind.Chii => new SetBlock(BlockKind.Run, m.Tiles.Min(), false),
                MeldKind.Pon => new SetBlock(BlockKind.Triplet, m.Tiles[0], false),
                _ => new SetBlock(BlockKind.Quad, m.Tiles[0], !m.IsOpen),
            };
        }

        List<YakuHan>? best = null;
        int bestHan = -1;
        bool bestYakuman = false;

        foreach ((int blockIndex, WaitShape shape) in WaitPlacements(d, ctx.WinTile))
        {
            // A triplet completed by ron was not concealed when it mattered.
            bool demote = shape == WaitShape.Shanpon && !ctx.Tsumo && blockIndex >= 0;
            if (demote)
                blocks[blockIndex] = blocks[blockIndex] with { Concealed = false };

            Consider(EvaluatePlacement(blocks, d.HeadTile, melds.Count, closed, total, shape, ctx),
                ref best, ref bestHan, ref bestYakuman);

            if (demote)
                blocks[blockIndex] = blocks[blockIndex] with { Concealed = true };
        }

        return best is null ? null : (best, bestYakuman);
    }

    private static IEnumerable<(int BlockIndex, WaitShape Shape)> WaitPlacements(Decomposition d, int win)
    {
        bool any = false;

        if (d.HeadTile == win)
        {
            any = true;
            yield return (-1, WaitShape.Tanki);
        }

        for (int i = 0; i < d.Sets.Count; i++)
        {
            SetBlock s = d.Sets[i];
            if (s.Kind == BlockKind.Triplet && s.Tile == win)
            {
                any = true;
                yield return (i, WaitShape.Shanpon);
            }
            else if (s.Kind == BlockKind.Run && win >= s.Tile && win <= s.Tile + 2)
            {
                any = true;
                WaitShape shape = win == s.Tile + 1
                    ? WaitShape.Kanchan
                    : win == s.Tile
                        ? (Tiles.Rank(s.Tile) == 7 ? WaitShape.Penchan : WaitShape.Ryanmen)
                        : (Tiles.Rank(s.Tile) == 1 ? WaitShape.Penchan : WaitShape.Ryanmen);
                yield return (i, shape);
            }
        }

        // Unknown/foreign win tile: still evaluate once with a shape that denies pinfu.
        if (!any)
            yield return (-2, WaitShape.Kanchan);
    }

    private static (List<YakuHan> Yaku, bool Yakuman)? EvaluatePlacement(
        SetBlock[] blocks, int head, int meldCount, bool closed, int[] total, WaitShape shape, in WinContext ctx)
    {
        int runCount = 0, quadCount = 0, concealedTriplets = 0, dragonTriplets = 0, windTriplets = 0;
        foreach (SetBlock b in blocks)
        {
            if (b.Kind == BlockKind.Run)
            {
                runCount++;
                continue;
            }

            if (b.Kind == BlockKind.Quad)
                quadCount++;
            if (b.Concealed)
                concealedTriplets++;
            if (b.Tile >= 31)
                dragonTriplets++;
            else if (b.Tile >= 27)
                windTriplets++;
        }

        bool allTerminalHonor = true, anyHonor = false, anyTerminal = false;
        var suits = new bool[3];
        for (int t = 0; t < Tiles.TypeCount; t++)
        {
            if (total[t] == 0)
                continue;
            if (Tiles.IsHonor(t))
                anyHonor = true;
            else
            {
                suits[(int)Tiles.SuitOf(t)] = true;
                if (Tiles.IsTerminal(t))
                    anyTerminal = true;
                else
                    allTerminalHonor = false;
            }
        }

        int suitCount = (suits[0] ? 1 : 0) + (suits[1] ? 1 : 0) + (suits[2] ? 1 : 0);

        var yakuman = new List<YakuHan>();
        if (concealedTriplets == 4)
            yakuman.Add(new(Yaku.Suuankou, YakumanHan));
        if (quadCount == 4)
            yakuman.Add(new(Yaku.Suukantsu, YakumanHan));
        if (dragonTriplets == 3)
            yakuman.Add(new(Yaku.Daisangen, YakumanHan));
        if (windTriplets == 4)
            yakuman.Add(new(Yaku.Daisuushii, YakumanHan));
        else if (windTriplets == 3 && head is >= 27 and <= 30)
            yakuman.Add(new(Yaku.Shousuushii, YakumanHan));
        if (allTerminalHonor)
        {
            if (!anyTerminal && anyHonor)
                yakuman.Add(new(Yaku.Tsuuiisou, YakumanHan));
            else if (!anyHonor)
                yakuman.Add(new(Yaku.Chinroutou, YakumanHan));
        }
        if (IsRyuuiisou(total))
            yakuman.Add(new(Yaku.Ryuuiisou, YakumanHan));
        if (closed && meldCount == 0 && suitCount == 1 && !anyHonor && IsChuuren(total))
            yakuman.Add(new(Yaku.ChuurenPoutou, YakumanHan));

        if (yakuman.Count > 0)
            return (yakuman, true);

        var yaku = new List<YakuHan>();

        if (closed && runCount == 4 && shape == WaitShape.Ryanmen && !IsYakuhaiTile(head, ctx))
            yaku.Add(new(Yaku.Pinfu, 1));

        if (!anyHonor && !anyTerminal)
            yaku.Add(new(Yaku.Tanyao, 1));

        foreach (SetBlock b in blocks)
        {
            if (b.Kind == BlockKind.Run)
                continue;
            switch (b.Tile)
            {
                case 31: yaku.Add(new(Yaku.Haku, 1)); break;
                case 32: yaku.Add(new(Yaku.Hatsu, 1)); break;
                case 33: yaku.Add(new(Yaku.Chun, 1)); break;
            }

            if (b.Tile == 27 + ctx.RoundWind)
                yaku.Add(new(Yaku.RoundWind, 1));
            if (b.Tile == 27 + ctx.SeatWind)
                yaku.Add(new(Yaku.SeatWind, 1));
        }

        if (closed)
        {
            int duplicateRunPairs = CountDuplicateRunPairs(blocks);
            if (duplicateRunPairs >= 2)
                yaku.Add(new(Yaku.Ryanpeiko, 3));
            else if (duplicateRunPairs == 1)
                yaku.Add(new(Yaku.Iipeiko, 1));
        }

        if (HasSanshokuDoujun(blocks))
            yaku.Add(new(Yaku.SanshokuDoujun, closed ? 2 : 1));
        if (HasSanshokuDoukou(blocks))
            yaku.Add(new(Yaku.SanshokuDoukou, 2));
        if (HasIttsu(blocks))
            yaku.Add(new(Yaku.Ittsu, closed ? 2 : 1));

        if (runCount >= 1 && AllBlocksHaveTerminalOrHonor(blocks, head))
        {
            if (!anyHonor)
                yaku.Add(new(Yaku.Junchan, closed ? 3 : 2));
            else
                yaku.Add(new(Yaku.Chanta, closed ? 2 : 1));
        }

        if (runCount == 0)
            yaku.Add(new(Yaku.Toitoi, 2));
        if (concealedTriplets == 3)
            yaku.Add(new(Yaku.Sanankou, 2));
        if (quadCount == 3)
            yaku.Add(new(Yaku.Sankantsu, 2));
        if (dragonTriplets == 2 && head >= 31)
            yaku.Add(new(Yaku.Shousangen, 2));
        if (allTerminalHonor && anyHonor && anyTerminal)
            yaku.Add(new(Yaku.Honroutou, 2));

        if (suitCount == 1)
        {
            yaku.Add(anyHonor
                ? new YakuHan(Yaku.Honitsu, closed ? 3 : 2)
                : new YakuHan(Yaku.Chinitsu, closed ? 6 : 5));
        }

        return (yaku, false);
    }

    private static (List<YakuHan> Yaku, bool Yakuman)? EvaluateChiitoi(int[] total)
    {
        bool allTerminalHonor = true, anyHonor = false, anyTerminal = false;
        var suits = new bool[3];
        for (int t = 0; t < Tiles.TypeCount; t++)
        {
            if (total[t] == 0)
                continue;
            if (Tiles.IsHonor(t))
                anyHonor = true;
            else
            {
                suits[(int)Tiles.SuitOf(t)] = true;
                if (Tiles.IsTerminal(t))
                    anyTerminal = true;
                else
                    allTerminalHonor = false;
            }
        }

        if (allTerminalHonor && anyHonor && !anyTerminal)
            return (new List<YakuHan> { new(Yaku.Tsuuiisou, YakumanHan) }, true);

        var yaku = new List<YakuHan> { new(Yaku.Chiitoitsu, 2) };
        if (!anyHonor && !anyTerminal)
            yaku.Add(new(Yaku.Tanyao, 1));
        if (allTerminalHonor)
            yaku.Add(new(Yaku.Honroutou, 2));

        int suitCount = (suits[0] ? 1 : 0) + (suits[1] ? 1 : 0) + (suits[2] ? 1 : 0);
        if (suitCount == 1)
            yaku.Add(anyHonor ? new YakuHan(Yaku.Honitsu, 3) : new YakuHan(Yaku.Chinitsu, 6));

        return (yaku, false);
    }

    private static void AddContextYaku(List<YakuHan> yaku, bool closed, in WinContext ctx)
    {
        if (closed && ctx.Tsumo)
            yaku.Add(new(Yaku.MenzenTsumo, 1));

        if (ctx.DoubleRiichi)
            yaku.Add(new(Yaku.DoubleRiichi, 2));
        else if (ctx.Riichi)
            yaku.Add(new(Yaku.Riichi, 1));

        if (ctx.Ippatsu && (ctx.Riichi || ctx.DoubleRiichi))
            yaku.Add(new(Yaku.Ippatsu, 1));

        if (ctx.Haitei && ctx.Tsumo)
            yaku.Add(new(Yaku.Haitei, 1));
        if (ctx.Houtei && !ctx.Tsumo)
            yaku.Add(new(Yaku.Houtei, 1));
        if (ctx.Rinshan && ctx.Tsumo)
            yaku.Add(new(Yaku.Rinshan, 1));
        if (ctx.Chankan && !ctx.Tsumo)
            yaku.Add(new(Yaku.Chankan, 1));
    }

    private static int CountDora(int[] total, in WinContext ctx)
    {
        int han = ctx.AkaCount;

        if (ctx.DoraIndicators is { } dora)
            foreach (int indicator in dora)
                han += total[DoraFromIndicator(indicator)];

        if ((ctx.Riichi || ctx.DoubleRiichi) && ctx.UraIndicators is { } ura)
            foreach (int indicator in ura)
                han += total[DoraFromIndicator(indicator)];

        return han;
    }

    private static bool IsYakuhaiTile(int tile, in WinContext ctx)
        => tile >= 31 || tile == 27 + ctx.RoundWind || tile == 27 + ctx.SeatWind;

    private static int CountDuplicateRunPairs(SetBlock[] blocks)
    {
        Span<int> runCounts = stackalloc int[Tiles.TypeCount];
        foreach (SetBlock b in blocks)
            if (b.Kind == BlockKind.Run)
                runCounts[b.Tile]++;

        int pairs = 0;
        foreach (int n in runCounts)
            pairs += n / 2;
        return pairs;
    }

    private static bool HasSanshokuDoujun(SetBlock[] blocks)
    {
        foreach (SetBlock a in blocks)
        {
            if (a.Kind != BlockKind.Run || a.Tile >= 9)
                continue;
            bool pin = false, sou = false;
            foreach (SetBlock b in blocks)
            {
                if (b.Kind != BlockKind.Run)
                    continue;
                if (b.Tile == a.Tile + 9)
                    pin = true;
                if (b.Tile == a.Tile + 18)
                    sou = true;
            }

            if (pin && sou)
                return true;
        }

        return false;
    }

    private static bool HasSanshokuDoukou(SetBlock[] blocks)
    {
        for (int rank = 0; rank < 9; rank++)
        {
            bool man = false, pin = false, sou = false;
            foreach (SetBlock b in blocks)
            {
                if (b.Kind == BlockKind.Run)
                    continue;
                if (b.Tile == rank)
                    man = true;
                if (b.Tile == rank + 9)
                    pin = true;
                if (b.Tile == rank + 18)
                    sou = true;
            }

            if (man && pin && sou)
                return true;
        }

        return false;
    }

    private static bool HasIttsu(SetBlock[] blocks)
    {
        for (int suit = 0; suit < 3; suit++)
        {
            int suitBase = suit * 9;
            bool low = false, mid = false, high = false;
            foreach (SetBlock b in blocks)
            {
                if (b.Kind != BlockKind.Run)
                    continue;
                if (b.Tile == suitBase)
                    low = true;
                if (b.Tile == suitBase + 3)
                    mid = true;
                if (b.Tile == suitBase + 6)
                    high = true;
            }

            if (low && mid && high)
                return true;
        }

        return false;
    }

    private static bool AllBlocksHaveTerminalOrHonor(SetBlock[] blocks, int head)
    {
        if (!Tiles.IsTerminalOrHonor(head))
            return false;

        foreach (SetBlock b in blocks)
        {
            bool ok = b.Kind == BlockKind.Run
                ? Tiles.Rank(b.Tile) is 1 or 7
                : Tiles.IsTerminalOrHonor(b.Tile);
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>All green: 2s, 3s, 4s, 6s, 8s, hatsu.</summary>
    private static bool IsRyuuiisou(int[] total)
    {
        for (int t = 0; t < Tiles.TypeCount; t++)
            if (total[t] > 0 && t is not (19 or 20 or 21 or 23 or 25 or 32))
                return false;
        return true;
    }

    /// <summary>1112345678999 + one extra of the same suit, fully concealed.</summary>
    private static bool IsChuuren(int[] total)
    {
        int suitBase = -1;
        for (int t = 0; t < 27; t++)
        {
            if (total[t] > 0)
            {
                suitBase = t / 9 * 9;
                break;
            }
        }

        if (suitBase < 0)
            return false;

        for (int r = 0; r < 9; r++)
        {
            int need = r is 0 or 8 ? 3 : 1;
            if (total[suitBase + r] < need)
                return false;
        }

        return true;
    }
}
