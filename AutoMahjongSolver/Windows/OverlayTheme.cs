using System.Numerics;
using AutoMahjongSolver.Windows.Sections.Config;

namespace AutoMahjongSolver.Windows;

/// <summary>
/// Colour palettes and labels for the in-game discard highlight, shared by the renderer
/// (<see cref="SuggestionOverlay"/>) and the settings UI so the two never drift. Each palette is a
/// vivid <see cref="Palette.Primary"/> for fills/glows and a brighter <see cref="Palette.Bright"/> for
/// edges and pointers, chosen to read on the busy 3-D mahjong board.
/// </summary>
internal static class OverlayTheme
{
    internal readonly record struct Palette(Vector4 Primary, Vector4 Bright);

    // Indexed by HighlightColor. Keep this order in lockstep with the enum.
    private static readonly Palette[] Palettes =
    {
        new(new(0.64f, 0.44f, 0.98f, 1f), new(0.82f, 0.66f, 1.00f, 1f)), // Violet
        new(new(0.20f, 0.82f, 0.98f, 1f), new(0.58f, 0.94f, 1.00f, 1f)), // Cyan
        new(new(0.36f, 0.90f, 0.50f, 1f), new(0.66f, 1.00f, 0.74f, 1f)), // Green
        new(new(1.00f, 0.76f, 0.24f, 1f), new(1.00f, 0.90f, 0.56f, 1f)), // Amber
        new(new(0.99f, 0.36f, 0.48f, 1f), new(1.00f, 0.62f, 0.70f, 1f)), // Rose
        new(new(0.98f, 0.40f, 0.86f, 1f), new(1.00f, 0.68f, 0.95f, 1f)), // Magenta
        new(new(0.97f, 0.98f, 1.00f, 1f), new(1.00f, 1.00f, 1.00f, 1f)), // White
    };

    public static Palette Resolve(HighlightColor color)
    {
        int i = (int)color;
        return i >= 0 && i < Palettes.Length ? Palettes[i] : Palettes[0];
    }

    /// <summary>The selectable primary colours, in enum order, for the settings swatch picker.</summary>
    public static readonly Vector4[] Swatches = Array.ConvertAll(Palettes, p => p.Primary);

    public static readonly SettingsControls.Choices.Choice[] StyleChoices =
    {
        new("Brackets", "Glowing corner brackets framing the tile, like a targeting reticle."),
        new("Arrow", "A bobbing arrow that points down at the tile from above."),
        new("Glow", "A soft pulsing halo around a filled tile tint."),
    };
}
