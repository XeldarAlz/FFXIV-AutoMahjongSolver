namespace Mahjong.Rules;

/// <summary>
/// Maps a dora indicator tile to the actual dora tile. Cycles within suits
/// and within wind/dragon groups (e.g. East→South→West→North→East,
/// haku→hatsu→chun→haku).
/// </summary>
public interface IDoraRule
{
    /// <summary>Resolve the dora tile from a dora indicator.</summary>
    Tile Next(Tile indicator);
}
