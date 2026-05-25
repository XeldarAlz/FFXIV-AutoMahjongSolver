using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

/// <summary>Managed marshalling of a single AtkValue slot so fixture replay doesn't need raw pointers.</summary>
internal readonly record struct AtkValueRecord(
    ValueType Type,
    int IntValue,
    uint UIntValue,
    string? StringValue)
{
    public bool IsInt => Type == ValueType.Int;
    public bool IsString => Type is ValueType.String or ValueType.String8 or ValueType.ManagedString;

    public static AtkValueRecord OfInt(int value) => new(ValueType.Int, value, 0, null);
    public static AtkValueRecord OfString(string value) => new(ValueType.String, 0, 0, value);
    public static AtkValueRecord Empty => default;
}
