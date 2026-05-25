using System;
using Dalamud.Configuration;

namespace Mahjong.Plugin.Dalamud;

/// <summary><see cref="Version"/> is mutable per the Dalamud interface; only the migration runner writes it.</summary>
[Serializable]
public sealed record Configuration : IPluginConfiguration
{
    public const int CurrentSchemaVersion = 2;

    public int Version { get; set; } = CurrentSchemaVersion;

    public bool AutomationArmed { get; init; } = false;

    public bool SuggestionOnly { get; init; } = true;

    public bool TosAccepted { get; init; } = false;

    public bool DevMode { get; init; } = false;

    /// <summary>Median delay (ms) between auto-play actions.</summary>
    public int HumanizedDelayMs { get; init; } = 1200;

    public bool ShowInGameHighlight { get; init; } = true;

    public HighlightStyle HighlightStyle { get; init; } = HighlightStyle.NeonGlow;

    /// <summary>RGB color for hand-discard picks. Defaults to Theme.Accent.</summary>
    public RgbColor HighlightColorDiscard { get; init; } = RgbColor.Defaults.Discard;

    /// <summary>RGB color for tsumogiri (drawn-tile) picks. Defaults to Theme.Warn.</summary>
    public RgbColor HighlightColorTsumogiri { get; init; } = RgbColor.Defaults.Tsumogiri;

    /// <summary>Multiplier on overlay pulse alpha. 1.0 is default; 0.5 dims, 1.5 boosts.</summary>
    public float HighlightIntensity { get; init; } = 1.0f;

    public bool ShowSuggestionDetails { get; init; } = false;

    /// <summary>Sticky once the user accepts the first-arming auto-play warning.</summary>
    public bool AutoPlayConfirmed { get; init; } = false;

    public bool EnableGameLogging { get; init; } = true;

    /// <summary>
    /// Stable anonymous install identifier sent as <c>X-Install-Id</c>. <see cref="Guid.Empty"/>
    /// = not yet minted; the uploader treats that as a fatal init error.
    /// </summary>
    public Guid InstallId { get; init; } = Guid.Empty;
}

/// <summary>Visual treatment for the in-game best-tile overlay.</summary>
public enum HighlightStyle
{
    NeonGlow = 0,
    Spotlight = 1,
    Arrow = 2,
}

/// <summary>Init-only property record (not positional) so Newtonsoft.Json round-trips it cleanly.</summary>
public sealed record RgbColor
{
    public float R { get; init; }
    public float G { get; init; }
    public float B { get; init; }

    public System.Numerics.Vector3 ToVector3() => new(R, G, B);

    public static RgbColor From(System.Numerics.Vector3 v) => new() { R = v.X, G = v.Y, B = v.Z };

    public static class Defaults
    {
        public static readonly RgbColor Discard = new() { R = 0.28f, G = 0.82f, B = 0.62f };
        public static readonly RgbColor Tsumogiri = new() { R = 0.98f, G = 0.80f, B = 0.30f };
    }
}
