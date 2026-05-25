using System;
using System.IO;

namespace Mahjong.Plugin.Dalamud.Tests.Stubs;

public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "Mahjong.Plugin.Dalamud.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(Path, recursive: true); }
        catch { }
    }
}
