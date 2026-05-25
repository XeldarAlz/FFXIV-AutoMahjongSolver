using System.Text.Json;

namespace Mahjong.Replay;

/// <summary>
/// Tenhou 136-id = suit_base + number*4 + copy_index. Red-5 (aka) is copy 0 at ids 16/52/88;
/// the aka bit is lost when mapped to 34-space — acceptable for ukeire/shanten, drops a dora.
/// </summary>
public static class TenhouLog
{
    public const int RedFiveMan = 16;
    public const int RedFivePin = 52;
    public const int RedFiveSou = 88;
    private const int Tenhou136Count = 136;

    public static Tile From136(int pai)
    {
        if (pai < 0 || pai >= Tenhou136Count)
            throw new ArgumentOutOfRangeException(nameof(pai), $"expected 0..{Tenhou136Count - 1}, got {pai}");
        return Tile.FromId(pai / 4);
    }

    public static bool IsRed5(int pai) => pai == RedFiveMan || pai == RedFivePin || pai == RedFiveSou;

    public enum EventKind { None, Riichi, Pon, Chi, Kan, Agari, Other }

    public readonly record struct Event(EventKind Kind, string RawTag, int TileId);

    public readonly record struct Kyoku(
        int Round,
        int Dealer,
        int Honba,
        int RiichiSticks,
        int[] StartScores,
        Tile[] DoraIndicators,
        Tile[][] StartingHands,
        int[][] DrawTiles,
        int[][] DiscardTiles,
        Event[][] DrawEvents,
        Event[][] DiscardEvents);

    public static Kyoku ParseKyoku(JsonElement kyoku)
    {
        if (kyoku.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("expected JSON array for kyoku");

        var (round, dealer, honba, riichiSticks) = ParseRoundInfo(kyoku[0]);
        var startScores = ParseScores(kyoku[1]);
        var dora = ParseTileArray(kyoku[2]);

        var hands = new Tile[4][];
        var draws = new int[4][];
        var discards = new int[4][];
        var drawEvents = new Event[4][];
        var discardEvents = new Event[4][];
        for (int seat = 0; seat < 4; seat++)
        {
            int baseIdx = 4 + seat * 3;
            hands[seat] = ParseTileArray(kyoku[baseIdx]);
            (draws[seat], drawEvents[seat]) = ParseEventArray(kyoku[baseIdx + 1]);
            (discards[seat], discardEvents[seat]) = ParseEventArray(kyoku[baseIdx + 2]);
        }

        return new Kyoku(
            Round: round,
            Dealer: dealer,
            Honba: honba,
            RiichiSticks: riichiSticks,
            StartScores: startScores,
            DoraIndicators: dora,
            StartingHands: hands,
            DrawTiles: draws,
            DiscardTiles: discards,
            DrawEvents: drawEvents,
            DiscardEvents: discardEvents);
    }

    public static Kyoku[] ParseDocument(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("log", out var log))
            throw new ArgumentException("Tenhou JSON has no 'log' field");

        var result = new List<Kyoku>();
        foreach (var kyoku in log.EnumerateArray())
            result.Add(ParseKyoku(kyoku));
        return result.ToArray();
    }

    private static (int round, int dealer, int honba, int riichiSticks) ParseRoundInfo(JsonElement el)
    {
        int roundDealerHonba = el[0].GetInt32();
        int honba = el[1].GetInt32();
        int riichiSticks = el[2].GetInt32();
        return (round: roundDealerHonba / 4, dealer: roundDealerHonba % 4, honba, riichiSticks);
    }

    private static int[] ParseScores(JsonElement el)
    {
        var scores = new int[4];
        for (int i = 0; i < 4 && i < el.GetArrayLength(); i++)
            scores[i] = el[i].GetInt32();
        return scores;
    }

    private static Tile[] ParseTileArray(JsonElement arr)
    {
        var result = new List<Tile>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number)
                result.Add(From136(el.GetInt32()));
        }
        return result.ToArray();
    }

    /// <summary>
    /// Tags: "r60"=riichi discard, "p..."=pon, "c..."=chi, "m"/"k"/"a..."=kan variants.
    /// Numbers are plain tile IDs.
    /// </summary>
    private static (int[] tiles, Event[] events) ParseEventArray(JsonElement arr)
    {
        var tiles = new List<int>();
        var events = new List<Event>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                int id = From136(el.GetInt32()).Id;
                tiles.Add(id);
                events.Add(new Event(EventKind.None, string.Empty, id));
                continue;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                string tag = el.GetString() ?? string.Empty;
                var (kind, tileId) = ParseEventTag(tag);
                tiles.Add(tileId);
                events.Add(new Event(kind, tag, tileId));
            }
        }
        return (tiles.ToArray(), events.ToArray());
    }

    public static (EventKind kind, int tileId) ParseEventTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return (EventKind.Other, -1);

        char prefix = char.ToLowerInvariant(tag[0]);
        var kind = prefix switch
        {
            'r' => EventKind.Riichi,
            'p' => EventKind.Pon,
            'c' => EventKind.Chi,
            'm' or 'k' or 'a' => EventKind.Kan,
            _ => EventKind.Other,
        };

        if (!TryExtractFirstIntegerBlock(tag, out int pai136))
            return (kind, -1);
        if (pai136 < 0 || pai136 >= Tenhou136Count)
            return (kind, -1);
        return (kind, From136(pai136).Id);
    }

    private static bool TryExtractFirstIntegerBlock(string tag, out int value)
    {
        int start = 0;
        while (start < tag.Length && !char.IsDigit(tag[start]))
            start++;
        int end = start;
        while (end < tag.Length && char.IsDigit(tag[end]))
            end++;
        if (start == end)
        {
            value = -1;
            return false;
        }
        return int.TryParse(tag.AsSpan(start, end - start), out value);
    }
}
