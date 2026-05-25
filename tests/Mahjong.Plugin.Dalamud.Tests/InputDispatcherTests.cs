using Mahjong.Plugin.Dalamud.Actions;

namespace Mahjong.Plugin.Dalamud.Tests;

public class InputDispatcherTests
{
    [Fact]
    public void Throws_when_addon_is_null()
    {
        Assert.Throws<ArgumentNullException>(() => new InputDispatcher(null!));
    }
}
