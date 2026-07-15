using System.Text.Json;

namespace Medoz.Studio.Settings;

/// <summary>
/// data/studio.json の読み書き (docs/studio-architecture.md)。
/// UI で明示的に変更した項目だけを保存する。CLI (Live -- --console) はこのファイルを読まない。
/// </summary>
public sealed class StudioSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _lock = new();
    private StudioSettingsData _data;

    public StudioSettingsStore(string path = "data/studio.json")
    {
        _path = path;
        _data = Load(path);
    }

    /// <summary>現在の保存済み設定のスナップショット。</summary>
    public StudioSettingsData Current()
    {
        lock (_lock)
        {
            return _data.Clone();
        }
    }

    /// <summary>設定を変更して保存する。mutate には UI で触った項目だけをセットさせる。</summary>
    public StudioSettingsData Update(Action<StudioSettingsData> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        lock (_lock)
        {
            mutate(_data);
            Save();
            return _data.Clone();
        }
    }

    private static StudioSettingsData Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<StudioSettingsData>(File.ReadAllText(path)) ?? new StudioSettingsData();
            }
        }
        catch (JsonException)
        {
            // 壊れた studio.json は無視して初期状態から始める (設定変更時に上書き保存される)
        }
        return new StudioSettingsData();
    }

    private void Save()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
    }
}
