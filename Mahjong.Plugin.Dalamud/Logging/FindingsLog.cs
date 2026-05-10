using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Mahjong.Plugin.Dalamud.Logging;

/// <summary>
/// Structured "findings" channel: one append-only NDJSON per day under
/// <c>pluginConfigs/&lt;plugin&gt;/findings/findings-yyyyMMdd.ndjson</c>.
/// Records the plugin's runtime discoveries that are otherwise lost to
/// <c>Plugin.Log.Info</c> ephemera — variant probes, sigscan results, addon
/// field-read failures, anything that helps reverse-engineer the addon
/// across clients.
///
/// <para>Each entry is a single JSON object with a stable <c>kind</c> field
/// the server can shard on (e.g. <c>variant_match</c>, <c>variant_miss</c>,
/// <c>sigscan_hit</c>, <c>field_read_fail</c>) plus a free-form
/// <c>data</c> bag. The schema is intentionally loose so new finding kinds
/// don't require a schema bump on the server.</para>
/// </summary>
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
            // Findings failures funnel into the error sink so we still see
            // them in the corpus. ErrorSink itself can't recursively fail
            // (it swallows everything internally).
            errors.RecordException("FindingsLog.WriteEntry", ex);
        }
    }

    private static string NowIso() =>
        DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    // Defense in depth against PII leaking through this sink: scrub every
    // string value in the data bag and the free-form note. Callers (e.g.
    // AddonEmjReader) are expected to pre-redact, but a future caller that
    // forgets — or a future field added with an absolute path in error —
    // shouldn't leak the user's home directory to the corpus.
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
        // Windows drive prefix or POSIX absolute path. Cheap precheck — full
        // redactor handles the "preserve trailing segments" rules.
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
