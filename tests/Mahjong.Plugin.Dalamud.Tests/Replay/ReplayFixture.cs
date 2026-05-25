using System.Collections.Generic;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

/// <summary>Serialization shape for a Track 0 replay fixture. One JSON file per scenario.</summary>
public sealed class ReplayFixture
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Variant { get; set; } = string.Empty;

    /// <summary>Base64-encoded addon-memory buffer. Telemetry-captured fixtures carry the raw bytes from the game's AtkUnitBase.</summary>
    public string AddonMemoryBase64 { get; set; } = string.Empty;

    public List<ReplayAtkValue> AtkValues { get; set; } = new();
    public bool CallModalVisible { get; set; }
    public List<string>? ListWidgetLabels { get; set; }

    public ReplayExpected Expected { get; set; } = new();
}

/// <summary>One AtkValue slot. Telemetry's JSON encoding represents the union by which field is populated.</summary>
public sealed class ReplayAtkValue
{
    /// <summary>One of: Int, UInt, Bool, String, String8, ManagedString.</summary>
    public string Type { get; set; } = "Int";
    public int? Int { get; set; }
    public uint? UInt { get; set; }
    public bool? Bool { get; set; }
    public string? String { get; set; }
}

public sealed class ReplayExpected
{
    public int? StateCode { get; set; }
    /// <summary>Tile string accepted by <see cref="Mahjong.Core.Tiles.Parse"/> (e.g. "123m456p789s1234z"). Null skips the assertion.</summary>
    public string? Hand { get; set; }
    /// <summary>Flag names matching <see cref="Mahjong.Engine.ActionFlags"/>; combined with bitwise-OR.</summary>
    public List<string>? LegalFlags { get; set; }
    public int? ScoreSelf { get; set; }
    public int? WallRemaining { get; set; }
    public int? AkaDora { get; set; }
    public int? MeldCount { get; set; }
}
