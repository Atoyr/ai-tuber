using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Medoz.AiTuber.Core;

/// <summary>
/// ペルソナパッケージのロード・検証に失敗したときの例外。
/// 起動時に fail fast し、不足しているファイルと場所をメッセージに含める
/// (AudioPlayer のデバイス一覧例外と同じ UX 方針)。
/// </summary>
public class PersonaLoadException : Exception
{
    public PersonaLoadException(string message) : base(message) { }
    public PersonaLoadException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>persona.json の voice セクション。声もキャラの一部としてペルソナ側が持つ</summary>
public record PersonaVoice
{
    /// <summary>VOICEVOX の話者ID</summary>
    [JsonPropertyName("speakerId")]
    public int? SpeakerId { get; init; }

    /// <summary>感情タグ (character.md の規約) → VOICEVOX スタイルID。キーは小文字に正規化される</summary>
    [JsonPropertyName("emotionStyles")]
    public Dictionary<string, int>? EmotionStyles { get; init; }
}

/// <summary>persona.json (マニフェスト)。フォーマットの正式契約は docs/persona-architecture.md</summary>
public record PersonaManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    /// <summary>表示名。コンソールログのラベルに使う</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>^[a-z0-9-]+$。メモリの保存先パス (data/&lt;slug&gt;/memory.json) などマシン用途の識別子</summary>
    [JsonPropertyName("slug")]
    public string Slug { get; init; } = "";

    [JsonPropertyName("voice")]
    public PersonaVoice? Voice { get; init; }

    /// <summary>ペルソナ固有の追加禁止ワード。エンジン共通セットに追加マージされる (共通セットは外せない)</summary>
    [JsonPropertyName("bannedWords")]
    public List<string>? BannedWords { get; init; }
}

/// <summary>
/// ペルソナパッケージ (persona.json + character.md + モード別md のディレクトリ) のロードと検証。
/// エンジンはこの契約だけを知り、人格の中身は外部ディレクトリ/別リポジトリで管理する
/// (設計: docs/persona-architecture.md)。
/// </summary>
public class PersonaPackage
{
    public const int SupportedSchemaVersion = 1;

    public string DirectoryPath { get; }
    public PersonaManifest Manifest { get; }

    /// <summary>character.md (共通人格 = キャラの魂)</summary>
    public string CharacterPrompt { get; }

    private PersonaPackage(string directoryPath, PersonaManifest manifest, string characterPrompt)
    {
        DirectoryPath = directoryPath;
        Manifest = manifest;
        CharacterPrompt = characterPrompt;
    }

    /// <summary>
    /// ペルソナディレクトリを検証してロードする。
    /// modeFile を渡すと、そのモードのmdの存在も起動時に検証する (fail fast)。
    /// </summary>
    public static PersonaPackage Load(string personaDir, string? modeFile = null)
    {
        if (!Directory.Exists(personaDir))
        {
            throw new PersonaLoadException(
                $"ペルソナディレクトリが見つかりません: {Path.GetFullPath(personaDir)}\n" +
                "環境変数 PERSONA_DIR でペルソナパッケージ (docs/persona-architecture.md) の場所を指定してください。");
        }

        string manifestPath = Path.Combine(personaDir, "persona.json");
        if (!File.Exists(manifestPath))
        {
            throw new PersonaLoadException(
                $"persona.json が見つかりません: {Path.GetFullPath(manifestPath)}\n" +
                "ペルソナパッケージにはマニフェスト persona.json が必須です (docs/persona-architecture.md)。");
        }

        PersonaManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PersonaManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            throw new PersonaLoadException($"persona.json のパースに失敗しました: {manifestPath} ({ex.Message})", ex);
        }
        if (manifest is null)
        {
            throw new PersonaLoadException($"persona.json が空です: {manifestPath}");
        }
        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            throw new PersonaLoadException(
                $"未対応の schemaVersion です: {manifest.SchemaVersion} (このエンジンは {SupportedSchemaVersion} に対応): {manifestPath}");
        }
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new PersonaLoadException($"persona.json の name が未設定です: {manifestPath}");
        }
        if (!Regex.IsMatch(manifest.Slug, "^[a-z0-9-]+$"))
        {
            throw new PersonaLoadException($"persona.json の slug は ^[a-z0-9-]+$ にしてください: \"{manifest.Slug}\" ({manifestPath})");
        }
        if (manifest.Voice?.SpeakerId is null)
        {
            throw new PersonaLoadException($"persona.json の voice.speakerId が未設定です: {manifestPath}");
        }
        if (manifest.Voice.EmotionStyles is null)
        {
            throw new PersonaLoadException($"persona.json の voice.emotionStyles が未設定です (空のマップ {{}} は可): {manifestPath}");
        }

        // 感情タグは小文字で扱う (VOICEVOX_EMOTION_STYLES の環境変数上書きと同じ正規化)
        manifest = manifest with
        {
            Voice = manifest.Voice with
            {
                EmotionStyles = manifest.Voice.EmotionStyles
                    .ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value),
            },
        };

        string characterPath = Path.Combine(personaDir, "character.md");
        if (!File.Exists(characterPath))
        {
            throw new PersonaLoadException(
                $"character.md (共通人格) が見つかりません: {Path.GetFullPath(characterPath)}");
        }

        var package = new PersonaPackage(personaDir, manifest, File.ReadAllText(characterPath));
        if (modeFile is not null)
        {
            package.LoadModePrompt(modeFile); // 存在検証。無ければ明確な例外で起動中止
        }
        return package;
    }

    /// <summary>モード別プロンプトを読み込む。無いモードを起動したら fail fast</summary>
    public string LoadModePrompt(string modeFile)
    {
        string path = Path.Combine(DirectoryPath, modeFile);
        if (!File.Exists(path))
        {
            throw new PersonaLoadException(
                $"このペルソナ ({Manifest.Name}) には {modeFile} がありません: {Path.GetFullPath(path)}\n" +
                $"このモードを使うには {modeFile} をペルソナパッケージに追加してください。");
        }
        return File.ReadAllText(path);
    }

    /// <summary>character.md (共通人格) + モード別指示 を結合してシステムプロンプトにする</summary>
    public string BuildSystemPrompt(string modeFile)
        => $"{CharacterPrompt}\n\n---\n\n{LoadModePrompt(modeFile)}";
}
