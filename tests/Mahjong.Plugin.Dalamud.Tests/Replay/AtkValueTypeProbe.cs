using System;
using Xunit.Abstractions;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

/// <summary>One-shot probe to capture AtkValueType integer values for the tools/extract-fixture.mjs decoder. Output via test runner --logger "console;verbosity=detailed".</summary>
public class AtkValueTypeProbe
{
    private readonly ITestOutputHelper output;

    public AtkValueTypeProbe(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Print_enum_values()
    {
        foreach (var name in Enum.GetNames<ValueType>())
        {
            var value = (int)Enum.Parse<ValueType>(name);
            output.WriteLine($"{name,-20}\t{value}\t0x{value:X2}");
        }
    }
}
