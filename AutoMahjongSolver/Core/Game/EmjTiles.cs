using System.Text.RegularExpressions;

namespace AutoMahjongSolver.Core.Game;

/// <summary>
/// Maps the Doman Mahjong tile texture icon ids to the engine's 0-33 tile index.
/// Icons live under ui/icon/076000/. Clean-room reference data, last validated ~patch 7.2;
/// re-confirm with /amjs dump on the live client. Red fives are distinct icon ids, not a flag.
/// </summary>
internal static partial class EmjTiles
{
    public const int RedFiveMan = 4;   // 5m
    public const int RedFivePin = 13;  // 5p
    public const int RedFiveSou = 22;  // 5s

    /// <summary>Resolve a texture icon id (e.g. 76041) to a tile index and red-five flag.</summary>
    public static bool TryFromIconId(int iconId, out int tileIndex, out bool isAka)
    {
        tileIndex = -1;
        isAka = false;

        switch (iconId)
        {
            case >= 76041 and <= 76049: tileIndex = iconId - 76041; return true;       // 1-9m  -> 0-8
            case >= 76050 and <= 76058: tileIndex = 9 + (iconId - 76050); return true;  // 1-9p  -> 9-17
            case >= 76059 and <= 76067: tileIndex = 18 + (iconId - 76059); return true; // 1-9s  -> 18-26
            case >= 76068 and <= 76074: tileIndex = 27 + (iconId - 76068); return true; // 1-7z  -> 27-33
            case 76075: tileIndex = RedFiveMan; isAka = true; return true;
            case 76076: tileIndex = RedFivePin; isAka = true; return true;
            case 76077: tileIndex = RedFiveSou; isAka = true; return true;
            default: return false;
        }
    }

    /// <summary>
    /// Canonical texture icon id for a tile index (the inverse of <see cref="TryFromIconId"/>), e.g.
    /// 0 -> 76041. With <paramref name="aka"/> set on a five, returns that suit's red-five id. -1 if out of range.
    /// </summary>
    public static int ToIconId(int tileIndex, bool aka = false)
    {
        if (aka)
        {
            switch (tileIndex)
            {
                case RedFiveMan: return 76075;
                case RedFivePin: return 76076;
                case RedFiveSou: return 76077;
            }
        }

        return tileIndex switch
        {
            >= 0 and <= 8 => 76041 + tileIndex,           // 1-9m
            >= 9 and <= 17 => 76050 + (tileIndex - 9),    // 1-9p
            >= 18 and <= 26 => 76059 + (tileIndex - 18),  // 1-9s
            >= 27 and <= 33 => 76068 + (tileIndex - 27),  // 1-7z
            _ => -1,
        };
    }

    /// <summary>Whether an icon id is one of the three red fives.</summary>
    public static bool IsAkaIcon(int iconId) => iconId is >= 76075 and <= 76077;

    /// <summary>Extract the icon id from a texture file path (e.g. "ui/icon/076000/076044_hr1.tex" -> 76044).</summary>
    public static bool TryIconId(string? path, out int iconId)
    {
        iconId = -1;
        if (string.IsNullOrEmpty(path))
            return false;

        Match m = IconNumber().Match(path);
        return m.Success && int.TryParse(m.Value, out iconId);
    }

    /// <summary>Resolve a texture file path (e.g. "ui/icon/076000/076041_hr1.tex") to a tile.</summary>
    public static bool TryFromTexturePath(string? path, out int tileIndex, out bool isAka)
    {
        tileIndex = -1;
        isAka = false;
        return TryIconId(path, out int iconId) && TryFromIconId(iconId, out tileIndex, out isAka);
    }

    // Match the icon number in the FILE name (076044_hr1.tex / 076044.tex), not the folder (076000/).
    // The file number is followed by '_' or '.'; the folder is followed by '/'.
    [GeneratedRegex(@"0760\d{2}(?=[_.])")]
    private static partial Regex IconNumber();
}
