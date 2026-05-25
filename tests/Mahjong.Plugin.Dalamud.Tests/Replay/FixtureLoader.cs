using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

internal static class FixtureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ReplayFixture Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}", path);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReplayFixture>(json, JsonOptions)
            ?? throw new InvalidDataException($"Fixture {path} deserialized as null");
    }

    public static IReadOnlyList<(string Path, ReplayFixture Fixture)> LoadAll(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Directory.Exists(directory))
            return [];
        var fixtures = new List<(string, ReplayFixture)>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            fixtures.Add((path, Load(path)));
        return fixtures;
    }
}
