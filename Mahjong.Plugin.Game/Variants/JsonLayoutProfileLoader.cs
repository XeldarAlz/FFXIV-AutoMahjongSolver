using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mahjong.Plugin.Game.Variants;

/// <summary>Numeric fields accept decimal or hex strings ("0x0500") for memory-offset readability.</summary>
public static class JsonLayoutProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new HexAwareIntConverter(), new HexAwareUIntConverter() },
    };

    public static LayoutProfile Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Layout profile not found: {path}", path);

        var json = File.ReadAllText(path);
        return Parse(json, sourceForErrorReporting: path);
    }

    public static LayoutProfile Parse(string json, string sourceForErrorReporting = "<inline>")
    {
        ArgumentNullException.ThrowIfNull(json);
        var profile = JsonSerializer.Deserialize<LayoutProfile>(json, JsonOptions)
            ?? throw new InvalidDataException(
                $"Layout profile {sourceForErrorReporting} deserialized as null");
        return profile;
    }

    public static IReadOnlyList<LayoutProfile> LoadAll(string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        if (!Directory.Exists(directoryPath))
            return [];
        var profiles = new List<LayoutProfile>();
        foreach (var path in Directory.EnumerateFiles(directoryPath, "*.json"))
            profiles.Add(Load(path));
        return profiles;
    }

    private sealed class HexAwareIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.String => ParseHexOrDecimal(reader.GetString()),
                _ => throw new JsonException($"Expected number or hex string for int, got {reader.TokenType}"),
            };
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);

        private static int ParseHexOrDecimal(string? s)
        {
            if (string.IsNullOrEmpty(s))
                throw new JsonException("Empty string is not a valid integer");
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X", StringComparison.Ordinal)
                ? int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : int.Parse(s, CultureInfo.InvariantCulture);
        }
    }

    private sealed class HexAwareUIntConverter : JsonConverter<uint>
    {
        public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetUInt32(),
                JsonTokenType.String => ParseHexOrDecimal(reader.GetString()),
                _ => throw new JsonException($"Expected number or hex string for uint, got {reader.TokenType}"),
            };
        }

        public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);

        private static uint ParseHexOrDecimal(string? s)
        {
            if (string.IsNullOrEmpty(s))
                throw new JsonException("Empty string is not a valid integer");
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X", StringComparison.Ordinal)
                ? uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : uint.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
