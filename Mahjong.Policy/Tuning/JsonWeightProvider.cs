using System.IO;
using System.Text.Json;

namespace Mahjong.Policy.Tuning;

/// <summary>
/// Reads a <see cref="WeightBundle"/> from a weights.json file on disk. The
/// canonical replacement for the previous "tuner emits a C# Weights record
/// to copy-paste" workflow.
///
/// Behavior:
///   * If the file is missing, returns <see cref="WeightBundle.Default"/>.
///   * If the schema version mismatches, throws — silent migration is worse
///     than a loud failure when weights drift.
///
/// Hot-reload (<see cref="Changed"/> event) isn't implemented yet — Phase 3
/// only needs static loading. A FileSystemWatcher-based reload can land later.
/// </summary>
public sealed class JsonWeightProvider : IWeightProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly WeightBundle bundle;

    public WeightBundle Current => bundle;
    public event Action<WeightBundle>? Changed { add { } remove { } }

    public JsonWeightProvider(WeightBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        this.bundle = bundle;
    }

    public static JsonWeightProvider Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
            return new JsonWeightProvider(WeightBundle.Default);

        var json = File.ReadAllText(path);
        var bundle = JsonSerializer.Deserialize<WeightBundle>(json, JsonOptions)
            ?? throw new InvalidDataException($"weights.json at {path} deserialized as null");

        if (bundle.SchemaVersion != WeightBundle.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"weights.json schema version mismatch: file={bundle.SchemaVersion}, " +
                $"expected={WeightBundle.CurrentSchemaVersion}. Re-tune or migrate the file.");

        return new JsonWeightProvider(bundle);
    }

    public static void Save(string path, WeightBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bundle);
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }
}
