using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Mahjong.Plugin.Dalamud.GameState;

[StructLayout(LayoutKind.Explicit, Size = 0x300)]
public unsafe struct AddonEmj
{
    [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;
}
