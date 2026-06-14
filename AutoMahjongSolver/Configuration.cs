using Dalamud.Configuration;

namespace AutoMahjongSolver;

/// <summary>What engaging the cockpit does: Auto plays the hand for you; Suggest only shows the move and leaves you to play it.</summary>
public enum PlayMode
{
    Suggest,
    Auto,
}

/// <summary>The shape the in-game discard highlight takes (see <c>SuggestionOverlay</c>).</summary>
public enum HighlightStyle
{
    /// <summary>Glowing corner brackets framing the tile — a targeting reticle.</summary>
    Brackets,

    /// <summary>A bobbing arrow pointing down at the tile from above.</summary>
    Arrow,

    /// <summary>A soft pulsing halo around a filled tile tint.</summary>
    Glow,
}

/// <summary>Colour palette for the in-game discard highlight; index maps to <c>OverlayTheme</c>.</summary>
public enum HighlightColor
{
    Violet,
    Cyan,
    Green,
    Amber,
    Rose,
    Magenta,
    White,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private const long SaveThrottleMs = 500;
    private long lastSaveTick;

    public int Version { get; set; } = 1;

    /// <summary>Auto plays the hand for you; Suggest only shows the recommended move and sends no inputs.</summary>
    public PlayMode Mode { get; set; } = PlayMode.Auto;

    /// <summary>In Suggest mode, paint a marker over the recommended discard tile on the live mahjong board.</summary>
    public bool HighlightSuggestedTile { get; set; } = true;

    /// <summary>Which shape the in-game discard highlight uses.</summary>
    public HighlightStyle HighlightStyle { get; set; } = HighlightStyle.Brackets;

    /// <summary>Which colour palette the in-game discard highlight uses.</summary>
    public HighlightColor HighlightColor { get; set; } = HighlightColor.Violet;

    // Auto-play humanized timing (ms). Ranges feed AutoPlayController.DelayFor so the bot never reacts instantly.
    public int TurnDelayMinMs { get; set; } = 700;
    public int TurnDelayMaxMs { get; set; } = 1800;
    public int CallDelayMinMs { get; set; } = 500;
    public int CallDelayMaxMs { get; set; } = 1200;
    public int ResultDelayMinMs { get; set; } = 1500;
    public int ResultDelayMaxMs { get; set; } = 2800;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>Coalesces the rapid-fire writes that sliders and drags produce into one save per throttle window.</summary>
    public void SaveDebounced()
    {
        long now = Environment.TickCount64;
        if (now - lastSaveTick < SaveThrottleMs)
        {
            return;
        }

        lastSaveTick = now;
        Save();
    }
}
