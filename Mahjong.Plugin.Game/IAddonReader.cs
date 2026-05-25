namespace Mahjong.Plugin.Game;

public interface IAddonReader
{
    Result<StateSnapshot, ReadError> Read();
}
