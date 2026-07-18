namespace Medoz.AiTuber.Core;

/// <summary>
/// 環境変数と定数の集約 (Python版 config.py 相当)。
/// 定数値は docs/architecture.md の「動作仕様」表と reference/python_v2/config.py に合わせる。
/// </summary>
public record AppConfig
{
    // --- ディレクトリ ---
    /// <summary>ペルソナパッケージのディレクトリ (環境変数 PERSONA_DIR。契約は docs/persona-architecture.md)</summary>
    public string PersonaDir { get; init; } = "personas/default";
    public string DataDir { get; init; } = "data";

    /// <summary>ペルソナごとのメモリ保存先 (slug は persona.json から)</summary>
    public string MemoryPathFor(string slug) => Path.Combine(DataDir, slug, "memory.json");

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

    /// <summary>
    /// 感情タグ (character.md 参照) → VOICEVOX スタイルID のマッピング (Phase H)。
    /// 既定は speaker 3 (ずんだもん) のスタイル。タグ無し・未知タグは <see cref="SpeakerId"/> にフォールバック。
    /// 環境変数 VOICEVOX_EMOTION_STYLES で "joy=1,sad=22" 形式の上書きが可能。
    /// </summary>
    public IReadOnlyDictionary<string, int> EmotionStyleIds { get; init; } = new Dictionary<string, int>
    {
        ["joy"] = 1,       // あまあま
        ["fun"] = 3,       // ノーマル
        ["sad"] = 22,      // ささやき
        ["angry"] = 7,     // ツンツン
        ["surprised"] = 3, // ノーマル
    };

    /// <summary>VOICEVOX_SPEAKER_ID が環境変数で明示されたか (persona.json より環境変数を優先するため)</summary>
    public bool SpeakerIdFromEnv { get; init; }

    /// <summary>VOICEVOX_EMOTION_STYLES が環境変数で明示されたか (persona.json より環境変数を優先するため)</summary>
    public bool EmotionStylesFromEnv { get; init; }

    /// <summary>ストリーミング (文単位合成 + 2キューTTS) を使うか。false で従来の一括生成。</summary>
    public bool UseStreaming { get; init; } = true;

    // --- YouTube Live ---
    public string YouTubeVideoId { get; init; } = "";
    /// <summary>YouTube Data API v3 の APIキー (liveChatMessages 取得に使用)</summary>
    public string YouTubeApiKey { get; init; } = "";
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

    // --- ブログ (ai-tuber-blogs) ---
    /// <summary>true なら実 push せず生成した markdown をコンソール表示のみ (デフォルトで dry-run)</summary>
    public bool BlogDryRun { get; init; } = true;
    /// <summary>ai-tuber-blogs リポジトリのローカルクローンパス (実 publish 時に必須)</summary>
    public string BlogRepoPath { get; init; } = "";
    /// <summary>重複回避のため記憶する直近ブログ記事数</summary>
    public int RecentPostsKeep { get; init; } = 10;

    // --- ゲーム実況 ---
    public int CaptureIntervalSec { get; init; } = 12;
    public int MaxImageWidth { get; init; } = 800;
    public int CommentaryHistoryLimit { get; init; } = 4;

    /// <summary>
    /// 実況1回の生成 maxTokens (環境変数 COMMENTARY_MAX_TOKENS)。
    /// Python版の 150 では日本語実況が単語レベルで途切れるため 500 に引き上げた
    /// (docs/architecture.md の動作仕様表参照)。
    /// </summary>
    public int CommentaryMaxTokens { get; init; } = 500;

    /// <summary>
    /// 実況に使うゲーム知識の名前 (環境変数 GAME_KNOWLEDGE)。
    /// ペルソナパッケージの knowledge/&lt;name&gt;.md をシステムプロンプトに結合する。空なら知識なし。
    /// </summary>
    public string GameKnowledge { get; init; } = "";

    /// <summary>実況対象ウィンドウのタイトル(部分一致)。Python版 game_commentary.py の WINDOW_TITLE_FRAGMENT 相当</summary>
    public string GameWindowTitle { get; init; } = "";

    /// <summary>
    /// キャプチャ方式 ("wgc" | "printwindow")。環境変数 CAPTURE_METHOD。
    /// 既定の wgc は Windows.Graphics.Capture (OBS の「Windows 10 (1903以降)」と同じ) で、
    /// 管理者権限のウィンドウ・隠れたウィンドウ・GPU描画のいずれも撮れる。
    /// printwindow は従来方式 (Windows 10 1903 未満へのフォールバック)。
    /// </summary>
    public string CaptureMethod { get; init; } = "wgc";

    /// <summary>禁止ワード (含まれていたら投稿・発話を破棄して作り直し)</summary>
    public IReadOnlyList<string> BannedWords { get; init; } = new[] { "死ね", "殺す", "http://", "@" };

    /// <summary>
    /// persona.json の値を反映する。優先順位: エンジンのデフォルト値 &lt; persona.json &lt; 環境変数。
    /// 禁止ワードはエンジン共通セットにペルソナ固有分を追加マージする (共通セットは外せない = 安全の下限)。
    /// </summary>
    public AppConfig ApplyPersona(PersonaManifest manifest)
    {
        var config = this with
        {
            BannedWords = BannedWords.Concat(manifest.BannedWords ?? Enumerable.Empty<string>())
                                     .Distinct().ToArray(),
        };
        if (!SpeakerIdFromEnv && manifest.Voice?.SpeakerId is int speakerId)
        {
            config = config with { SpeakerId = speakerId };
        }
        if (!EmotionStylesFromEnv && manifest.Voice?.EmotionStyles is { Count: > 0 } styles)
        {
            config = config with { EmotionStyleIds = styles };
        }
        return config;
    }

    /// <summary>環境変数から設定を読み込む。未設定の項目はデフォルト値のまま。</summary>
    public static AppConfig LoadFromEnvironment()
    {
        return new AppConfig
        {
            PersonaDir = Env("PERSONA_DIR", "personas/default"),
            LlmProvider = Env("LLM_PROVIDER", "claude"),
            AnthropicApiKey = Env("ANTHROPIC_API_KEY", ""),
            ClaudeModel = Env("CLAUDE_MODEL", "claude-sonnet-4-6"),
            GeminiApiKey = Env("GEMINI_API_KEY", ""),
            GeminiModel = Env("GEMINI_MODEL", "gemini-2.5-flash"),
            OpenAIApiKey = Env("OPENAI_API_KEY", ""),
            OpenAIModel = Env("OPENAI_MODEL", "gpt-4o"),
            VoicevoxUrl = Env("VOICEVOX_URL", "http://127.0.0.1:50021"),
            SpeakerId = EnvInt("VOICEVOX_SPEAKER_ID", 3),
            SpeakerIdFromEnv = Environment.GetEnvironmentVariable("VOICEVOX_SPEAKER_ID") is { Length: > 0 },
            EmotionStylesFromEnv = Environment.GetEnvironmentVariable("VOICEVOX_EMOTION_STYLES") is { Length: > 0 },
            OutputDeviceName = Env("VOICEVOX_OUTPUT_DEVICE", "CABLE Input"),
            YouTubeVideoId = Env("YT_VIDEO_ID", ""),
            YouTubeApiKey = Env("YOUTUBE_API_KEY", ""),
            GameWindowTitle = Env("WINDOW_TITLE_FRAGMENT", ""),
            CaptureMethod = Env("CAPTURE_METHOD", "wgc"),
            CommentaryMaxTokens = EnvInt("COMMENTARY_MAX_TOKENS", 500),
            GameKnowledge = Env("GAME_KNOWLEDGE", ""),
            XApiKey = Env("X_API_KEY", ""),
            XApiSecret = Env("X_API_SECRET", ""),
            XAccessToken = Env("X_ACCESS_TOKEN", ""),
            XAccessSecret = Env("X_ACCESS_SECRET", ""),
            // Python版: TWEET_DRY_RUN が "1" なら dry-run (未設定時も dry-run)
            TweetDryRun = Env("TWEET_DRY_RUN", "1") == "1",
            BlogDryRun = Env("BLOG_DRY_RUN", "1") == "1",
            BlogRepoPath = Env("BLOG_REPO_PATH", ""),
            EmotionStyleIds = ParseEmotionStyles(Env("VOICEVOX_EMOTION_STYLES", "")),
        };
    }

    /// <summary>
    /// "joy=1,sad=22" 形式の文字列を感情スタイルマップにする。空なら既定マップ。
    /// </summary>
    private static IReadOnlyDictionary<string, int> ParseEmotionStyles(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return new AppConfig().EmotionStyleIds;
        }
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] kv = pair.Split('=', 2);
            if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out int id))
            {
                map[kv[0].Trim().ToLowerInvariant()] = id;
            }
        }
        return map.Count > 0 ? map : new AppConfig().EmotionStyleIds;
    }

    private static string Env(string name, string defaultValue)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;

    private static int EnvInt(string name, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out int value) ? value : defaultValue;
}
