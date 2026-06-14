namespace AutoMahjongSolver.Engine;

public enum Suit
{
    Man = 0,
    Pin = 1,
    Sou = 2,
    Honor = 3,
}

/// <summary>
/// Tile identity helpers over the canonical 34-type index space:
/// 0-8 = 1m-9m, 9-17 = 1p-9p, 18-26 = 1s-9s, 27-33 = honors (E, S, W, N, White, Green, Red).
/// Red fives (aka dora) are the normal 5 of their suit with a separate aka flag; they are not a distinct index.
/// </summary>
public static class Tiles
{
    public const int TypeCount = 34;

    /// <summary>Terminal and honor indices, used for kokushi and yaku checks.</summary>
    public static readonly int[] TerminalsAndHonors = [0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33];

    public static Suit SuitOf(int index)
        => index < 9 ? Suit.Man : index < 18 ? Suit.Pin : index < 27 ? Suit.Sou : Suit.Honor;

    public static bool IsNumber(int index) => index >= 0 && index < 27;

    public static bool IsHonor(int index) => index >= 27 && index < TypeCount;

    /// <summary>1-9 within the suit for number tiles, 0 for honors.</summary>
    public static int Rank(int index) => IsNumber(index) ? (index % 9) + 1 : 0;

    public static bool IsTerminal(int index) => IsNumber(index) && (Rank(index) == 1 || Rank(index) == 9);

    public static bool IsTerminalOrHonor(int index) => IsHonor(index) || IsTerminal(index);

    /// <summary>
    /// Parse standard notation (e.g. "123m 456p 789s 1234567z") into a 34-length count array.
    /// '0' denotes a red five (counted as a 5; <paramref name="akaCount"/> is incremented). Whitespace is ignored.
    /// </summary>
    public static int[] ParseCounts(string notation, out int akaCount)
    {
        var counts = new int[TypeCount];
        akaCount = 0;
        var pending = new List<int>();

        foreach (char ch in notation)
        {
            if (char.IsWhiteSpace(ch))
                continue;

            if (ch is >= '0' and <= '9')
            {
                pending.Add(ch - '0');
                continue;
            }

            int suitBase = ch switch
            {
                'm' => 0,
                'p' => 9,
                's' => 18,
                'z' => 27,
                _ => throw new FormatException($"Unknown suit character '{ch}'."),
            };

            if (pending.Count == 0)
                throw new FormatException($"Suit '{ch}' had no preceding digits.");

            foreach (int digit in pending)
            {
                if (ch == 'z')
                {
                    if (digit is < 1 or > 7)
                        throw new FormatException($"Honor tile must be 1-7, got {digit}.");
                    counts[27 + digit - 1]++;
                }
                else
                {
                    int rank = digit;
                    if (digit == 0)
                    {
                        rank = 5;
                        akaCount++;
                    }

                    counts[suitBase + rank - 1]++;
                }
            }

            pending.Clear();
        }

        if (pending.Count > 0)
            throw new FormatException("Notation ended with digits that had no suit.");

        return counts;
    }

    public static int[] ParseCounts(string notation) => ParseCounts(notation, out _);

    /// <summary>Index of a single tile written in notation, e.g. "5m" -> 4, "1z" -> 27.</summary>
    public static int Index(string singleTile)
    {
        int[] counts = ParseCounts(singleTile);
        int found = -1;
        for (int i = 0; i < TypeCount; i++)
        {
            if (counts[i] == 0)
                continue;
            if (counts[i] != 1 || found >= 0)
                throw new FormatException($"'{singleTile}' is not a single tile.");
            found = i;
        }

        if (found < 0)
            throw new FormatException($"'{singleTile}' contained no tile.");
        return found;
    }

    /// <summary>Short label for a tile index, e.g. 4 -> "5m", 30 -> "4z".</summary>
    public static string Name(int index)
    {
        if (index is < 0 or >= TypeCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (IsHonor(index))
            return $"{index - 27 + 1}z";

        char suit = SuitOf(index) switch
        {
            Suit.Man => 'm',
            Suit.Pin => 'p',
            _ => 's',
        };
        return $"{Rank(index)}{suit}";
    }
}
