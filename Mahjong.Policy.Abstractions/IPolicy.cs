namespace Mahjong.Policy.Abstractions;

public interface IPolicy
{
    ActionChoice Choose(StateSnapshot state);
}
