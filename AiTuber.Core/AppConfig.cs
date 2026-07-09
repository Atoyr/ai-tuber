namespace Medoz.AiTuber.Core;

/// <summary>
/// 環境変数と定数の集約 (Python版 config.py 相当)。
/// 定数値は docs/architecture.md の「動作仕様」表と reference/python_v2/config.py に合わせる。
/// </summary>
public record AppConfig
{
    // --- ディレクトリ ---
    public string PromptDir { get; init; } = "prompts";
    public string DataDir { get; init; } = "data";
    public string MemoryPath => Path.Combine(DataDir, "memory.json");

    // --- LLM ---
    /// <summary>使用する LLM プロバイダ ("claude" | "gemini" | "openai")</summary>
    public string LlmProvider { get; init; } = "claude";

    public string AnthropicApiKey { get; init; } = "";
    public string ClaudeModel { get; init; } = "claude-sonnet-4-6";

    public string GeminiApiKey { get; init; } = "";
    public string GeminiModel { get; init; } = "gemini-2.5-flash";

    public string OpenAIApiKey { get; init; } = "";
    public string OpenAIModel { get; init; } = "gpt-4o";

    /// <summary>選択中のプロバイダの API キー</summary>
    public string LlmApiKey => LlmProvider.ToLower() switch
    {
        "gemini" => GeminiApiKey,
        "openai" => OpenAIApiKey,
        "claude" => AnthropicApiKey,
        _ => throw new InvalidOperationException($"未知の LLM プロバイダです: {LlmProvider}"),
    };

    /// <summary>選択中のプロバイダのモデル名</summary>
    public string LlmModel => LlmProvider.ToLower() switch
    {
        "gemini" => GeminiModel,
        "openai" => OpenAIModel,
        "claude" => ClaudeModel,
        _ => throw new InvalidOperationException($"未知の LLM プロバイダです: {LlmProvider}"),
    };

    /// <summary>API キーが未設定のときに表示する環境変数名</summary>
    public string LlmApiKeyEnvName => LlmProvider.ToLower() switch
    {
        "gemini" => "GEMINI_API_KEY",
        "openai" => "OPENAI_API_KEY",
        _ => "ANTHROPIC_API_KEY",
    };

    // --- VOICEVOX / 音声 ---
    public string VoicevoxUrl { get; init; } = "http://127.0.0.1:50021";
    public int SpeakerId { get; init; } = 3;
    public string OutputDeviceName { get; init; } = "CABLE Input";

    // --- YouTube Live ---
    public string YouTubeVideoId { get; init; } = "";
    public int CommentBatchSec { get; init; } = 4;
    public int FreetalkAfterSec { get; init; } = 45;
    public int HistoryTurns { get; init; } = 12;

    // --- Twitter (X API v2) ---
    public string XApiKey { get; init; } = "";
    public string XApiSecret { get; init; } = "";
    public string XAccessToken { get; init; } = "";
    public string XAccessSecret { get; init; } = "";

    public int TweetMinIntervalMin { get; init; } = 180;
    public int TweetMaxIntervalMin { get; init; } = 360;
    public int TweetActiveHourStart { get; init; } = 9;
    public int TweetActiveHourEnd { get; init; } = 24;
    /// <summary>true なら実投稿せずコンソール表示のみ (デフォルトで dry-run)</summary>
    public bool TweetDryRun { get; init; } = true;
    public int RecentTweetsKeep { get; init; } = 20;

    // --- ゲーム実況 ---
    public int CaptureIntervalSec { get; init; } = 12;
    public int MaxImageWidth { get; init; } = 800;
    public int CommentaryHistoryLimit { get; init; } = 4;

    /// <summary>禁止ワード (含まれていたら投稿・発話を破棄して作り直し)</summary>
    public IReadOnlyList<string> BannedWords { get; init; } = new[] { "死ね", "殺す", "http://", "@" };

    /// <summary>環境変数から設定を読み込む。未設定の項目はデフォルト値のまま。</summary>
    public static AppConfig LoadFromEnvironment()
    {
        return new AppConfig
        {
            LlmProvider = Env("LLM_PROVIDER", "claude"),
            AnthropicApiKey = Env("ANTHROPIC_API_KEY", ""),
            ClaudeModel = Env("CLAUDE_MODEL", "claude-sonnet-4-6"),
            GeminiApiKey = Env("GEMINI_API_KEY", ""),
            GeminiModel = Env("GEMINI_MODEL", "gemini-2.5-flash"),
            OpenAIApiKey = Env("OPENAI_API_KEY", ""),
            OpenAIModel = Env("OPENAI_MODEL", "gpt-4o"),
            VoicevoxUrl = Env("VOICEVOX_URL", "http://127.0.0.1:50021"),
            SpeakerId = EnvInt("VOICEVOX_SPEAKER_ID", 3),
            OutputDeviceName = Env("VOICEVOX_OUTPUT_DEVICE", "CABLE Input"),
            YouTubeVideoId = Env("YT_VIDEO_ID", ""),
            XApiKey = Env("X_API_KEY", ""),
            XApiSecret = Env("X_API_SECRET", ""),
            XAccessToken = Env("X_ACCESS_TOKEN", ""),
            XAccessSecret = Env("X_ACCESS_SECRET", ""),
            // Python版: TWEET_DRY_RUN が "1" なら dry-run (未設定時も dry-run)
            TweetDryRun = Env("TWEET_DRY_RUN", "1") == "1",
        };
    }

    private static string Env(string name, string defaultValue)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;

    private static int EnvInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : defaultValue;
}
