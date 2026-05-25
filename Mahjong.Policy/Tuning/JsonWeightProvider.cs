using System.IO;
using System.Text.Json;

namespace Mahjong.Policy.Tuning;

/// <summary>
/// Missing file returns <see cref="WeightBundle.Default"/>; schema mismatch throws —
/// silent migration is worse than loud failure when weights drift.
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
