using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Mahjong.Plugin.Dalamud.Logging;

public interface IFindingsLog
{
    void Record(string kind, IReadOnlyDictionary<string, object?> data);
    void Record(string kind, string note);
}

internal sealed class FindingsLog : IFindingsLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ErrorSink errors;
    private readonly string findingsDir;
    private readonly object writerLock = new();
    private long sequence;
    private bool disposed;

    public string FindingsDir => findingsDir;

    public FindingsLog(string pluginConfigDirectory, ErrorSink errors)
    {
        ArgumentException.ThrowIfNullOrEmpty(pluginConfigDirectory);
        ArgumentNullException.ThrowIfNull(errors);
        this.errors = errors;
        findingsDir = Path.Combine(pluginConfigDirectory, "findings");
        try
        { Directory.CreateDirectory(findingsDir); }
        catch { }
    }

    public void Dispose() => disposed = true;

    public void Record(string kind, IReadOnlyDictionary<string, object?> data)
    {
        if (disposed || string.IsNullOrEmpty(kind))
            return;
        WriteEntry(new FindingEntry(
            T: NowIso(),
            Seq: Interlocked.Increment(ref sequence),
            Kind: kind,
            Data: data,
            Note: null));
    }

    public void Record(string kind, string note)
    {
        if (disposed || string.IsNullOrEmpty(kind))
            return;
        WriteEntry(new FindingEntry(
            T: NowIso(),
            Seq: Interlocked.Increment(ref sequence),
            Kind: kind,
            Data: null,
            Note: note));
    }

    private void WriteEntry(FindingEntry entry)
    {
        try
        {
            entry = ScrubPaths(entry);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            var path = Path.Combine(findingsDir, $"findings-{DateTime.UtcNow:yyyyMMdd}.ndjson");
            lock (writerLock)
            {
                using var w = new StreamWriter(new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.Read));
                w.WriteLine(line);
            }
        }
        catch (Exception ex)
        {
            errors.RecordException("FindingsLog.WriteEntry", ex);
        }
    }

    private static string NowIso() =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    // Defense in depth — callers should pre-redact, but a forgotten path-shaped string must not leak the user's home directory.
    private static FindingEntry ScrubPaths(FindingEntry entry)
    {
        Dictionary<string, object?>? scrubbed = null;
        if (entry.Data is { Count: > 0 })
        {
            scrubbed = new Dictionary<string, object?>(entry.Data.Count);
            foreach (var kv in entry.Data)
                scrubbed[kv.Key] = ScrubValue(kv.Value);
        }
        var scrubbedNote = entry.Note is null ? null : ScrubIfPathLike(entry.Note);
        return entry with { Data = scrubbed, Note = scrubbedNote };
    }

    private static object? ScrubValue(object? value) => value switch
    {
        string s => ScrubIfPathLike(s),
        _ => value,
    };

    private static string ScrubIfPathLike(string s) =>
        LooksLikePath(s) ? PathRedactor.Redact(s) : s;

    private static bool LooksLikePath(string s)
    {
        if (s.Length < 3)
            return false;
        if (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/'))
            return true;
        if (s[0] == '/' || s[0] == '\\')
            return true;
        return false;
    }

    private sealed record FindingEntry(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("seq")] long Seq,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, object?>? Data,
        [property: JsonPropertyName("note")] string? Note);
}
