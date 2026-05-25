namespace Mahjong.Rules;

/// <summary>
/// Cycles within suits and within wind/dragon groups (East→South→West→North→East,
/// haku→hatsu→chun→haku).
/// </summary>
public interface IDoraRule
{
    Tile Next(Tile indicator);
}
