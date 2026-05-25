namespace Mahjong.Plugin.Game;

public interface IMeldRecorder
{
    IReadOnlyList<Meld> Current { get; }

    void Record(Meld meld);

    void ResetIfRoundEnded(int closedHandCount);
}
