using Medoz.AiTuber.Core;

namespace Medoz.AiTuber.Core.Tests;

public class AppConfigTests
{
    [Fact]
    public void Defaults_MatchArchitectureSpec()
    {
        var config = new AppConfig();

        Assert.Equal("claude-sonnet-4-6", config.ClaudeModel);
        Assert.Equal("http://127.0.0.1:50021", config.VoicevoxUrl);
        Assert.Equal(3, config.SpeakerId);
        Assert.Equal("CABLE Input", config.OutputDeviceName);
        Assert.Equal(4, config.CommentBatchSec);
        Assert.Equal(45, config.FreetalkAfterSec);
        Assert.Equal(12, config.HistoryTurns);
        Assert.Equal(180, config.TweetMinIntervalMin);
        Assert.Equal(360, config.TweetMaxIntervalMin);
        Assert.Equal(9, config.TweetActiveHourStart);
        Assert.Equal(24, config.TweetActiveHourEnd);
        Assert.True(config.TweetDryRun); // dry-run がデフォルト
        Assert.Equal(20, config.RecentTweetsKeep);
        Assert.Equal(12, config.CaptureIntervalSec);
        Assert.Equal(800, config.MaxImageWidth);
        Assert.Equal(4, config.CommentaryHistoryLimit);
        Assert.Equal(500, config.CommentaryMaxTokens);
        Assert.Equal("", config.GameKnowledge); // 未指定 = 知識なしで従来どおり動く
        Assert.Equal(new[] { "死ね", "殺す", "http://", "@" }, config.BannedWords);
        Assert.Equal("personas/default", config.PersonaDir);
        Assert.Equal(Path.Combine("data", "potofu", "memory.json"), config.MemoryPathFor("potofu"));
    }

    [Fact]
    public void LoadFromEnvironment_ReadsEnvironmentVariables()
    {
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("TWEET_DRY_RUN", "0");
            Environment.SetEnvironmentVariable("VOICEVOX_SPEAKER_ID", "8");

            var config = AppConfig.LoadFromEnvironment();

            Assert.Equal("test-key", config.AnthropicApiKey);
            Assert.False(config.TweetDryRun);
            Assert.Equal(8, config.SpeakerId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("TWEET_DRY_RUN", null);
            Environment.SetEnvironmentVariable("VOICEVOX_SPEAKER_ID", null);
        }
    }

    [Fact]
    public void LoadFromEnvironment_ReadsCommentarySettings()
    {
        try
        {
            Environment.SetEnvironmentVariable("COMMENTARY_MAX_TOKENS", "800");
            Environment.SetEnvironmentVariable("GAME_KNOWLEDGE", "minecraft");
            Environment.SetEnvironmentVariable("CAPTURE_INTERVAL_SEC", "20");

            var config = AppConfig.LoadFromEnvironment();

            Assert.Equal(800, config.CommentaryMaxTokens);
            Assert.Equal("minecraft", config.GameKnowledge);
            Assert.Equal(20, config.CaptureIntervalSec);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COMMENTARY_MAX_TOKENS", null);
            Environment.SetEnvironmentVariable("GAME_KNOWLEDGE", null);
            Environment.SetEnvironmentVariable("CAPTURE_INTERVAL_SEC", null);
        }
    }

    [Fact]
    public void LlmProvider_DefaultsToClaude()
    {
        var config = new AppConfig();

        Assert.Equal("claude", config.LlmProvider);
        Assert.Equal("claude-sonnet-4-6", config.LlmModel);
        Assert.Equal("ANTHROPIC_API_KEY", config.LlmApiKeyEnvName);
    }

    [Theory]
    [InlineData("claude", "anthropic-key", "claude-sonnet-4-6", "ANTHROPIC_API_KEY")]
    [InlineData("gemini", "gemini-key", "gemini-2.5-flash", "GEMINI_API_KEY")]
    [InlineData("openai", "openai-key", "gpt-4o", "OPENAI_API_KEY")]
    public void LlmApiKeyAndModel_ResolveByProvider(
        string provider, string expectedKey, string expectedModel, string expectedEnvName)
    {
        var config = new AppConfig
        {
            LlmProvider = provider,
            AnthropicApiKey = "anthropic-key",
            GeminiApiKey = "gemini-key",
            OpenAIApiKey = "openai-key",
        };

        Assert.Equal(expectedKey, config.LlmApiKey);
        Assert.Equal(expectedModel, config.LlmModel);
        Assert.Equal(expectedEnvName, config.LlmApiKeyEnvName);
    }

    [Fact]
    public void LlmApiKey_IsCaseInsensitive()
    {
        var config = new AppConfig { LlmProvider = "Gemini", GeminiApiKey = "gemini-key" };

        Assert.Equal("gemini-key", config.LlmApiKey);
    }

    [Fact]
    public void LlmApiKey_Throws_ForUnknownProvider()
    {
        var config = new AppConfig { LlmProvider = "llama" };

        Assert.Throws<InvalidOperationException>(() => config.LlmApiKey);
    }

    [Fact]
    public void LoadFromEnvironment_ReadsProviderAndPerProviderKeys()
    {
        try
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", "gemini");
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "g-key");
            Environment.SetEnvironmentVariable("GEMINI_MODEL", "gemini-2.5-pro");

            var config = AppConfig.LoadFromEnvironment();

            Assert.Equal("gemini", config.LlmProvider);
            Assert.Equal("g-key", config.LlmApiKey);
            Assert.Equal("gemini-2.5-pro", config.LlmModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_PROVIDER", null);
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
            Environment.SetEnvironmentVariable("GEMINI_MODEL", null);
        }
    }

    [Fact]
    public void LoadFromEnvironment_UsesDryRunDefault_WhenEnvNotSet()
    {
        Environment.SetEnvironmentVariable("TWEET_DRY_RUN", null);

        var config = AppConfig.LoadFromEnvironment();

        Assert.True(config.TweetDryRun);
    }

    [Fact]
    public void EmotionStyleIds_HasDefaultMapping()
    {
        var config = new AppConfig();

        Assert.Equal(1, config.EmotionStyleIds["joy"]);
        Assert.Equal(7, config.EmotionStyleIds["angry"]);
        Assert.True(config.UseStreaming); // ストリーミングが既定
    }

    [Fact]
    public void LoadFromEnvironment_ParsesEmotionStylesOverride()
    {
        try
        {
            Environment.SetEnvironmentVariable("VOICEVOX_EMOTION_STYLES", "joy=1, sad=22 ,angry=7");

            var config = AppConfig.LoadFromEnvironment();

            Assert.Equal(1, config.EmotionStyleIds["joy"]);
            Assert.Equal(22, config.EmotionStyleIds["sad"]);
            Assert.Equal(7, config.EmotionStyleIds["angry"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOICEVOX_EMOTION_STYLES", null);
        }
    }

    [Fact]
    public void LoadFromEnvironment_FallsBackToDefaultEmotionStyles_WhenEnvEmpty()
    {
        Environment.SetEnvironmentVariable("VOICEVOX_EMOTION_STYLES", null);

        var config = AppConfig.LoadFromEnvironment();

        Assert.Equal(new AppConfig().EmotionStyleIds["joy"], config.EmotionStyleIds["joy"]);
    }

    [Fact]
    public void LoadFromEnvironment_ReadsPersonaDir()
    {
        try
        {
            Environment.SetEnvironmentVariable("PERSONA_DIR", "../ai-tuber-persona-potofu");

            var config = AppConfig.LoadFromEnvironment();

            Assert.Equal("../ai-tuber-persona-potofu", config.PersonaDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PERSONA_DIR", null);
        }
    }

    private static PersonaManifest Manifest(int? speakerId = 5,
                                            Dictionary<string, int>? styles = null,
                                            List<string>? bannedWords = null) => new()
    {
        SchemaVersion = 1,
        Name = "テスト",
        Slug = "test",
        Voice = new PersonaVoice { SpeakerId = speakerId, EmotionStyles = styles },
        BannedWords = bannedWords,
    };

    [Fact]
    public void ApplyPersona_UsesPersonaVoice_WhenEnvNotSet()
    {
        var config = new AppConfig(); // SpeakerIdFromEnv = false

        var applied = config.ApplyPersona(Manifest(speakerId: 5, styles: new() { ["joy"] = 10 }));

        Assert.Equal(5, applied.SpeakerId);
        Assert.Equal(10, applied.EmotionStyleIds["joy"]);
    }

    [Fact]
    public void ApplyPersona_KeepsEnvValues_WhenEnvSet()
    {
        // 優先順位: エンジンデフォルト < persona.json < 環境変数
        var config = new AppConfig
        {
            SpeakerId = 8,
            SpeakerIdFromEnv = true,
            EmotionStylesFromEnv = true,
        };

        var applied = config.ApplyPersona(Manifest(speakerId: 5, styles: new() { ["joy"] = 10 }));

        Assert.Equal(8, applied.SpeakerId);
        Assert.Equal(new AppConfig().EmotionStyleIds["joy"], applied.EmotionStyleIds["joy"]);
    }

    [Fact]
    public void ApplyPersona_KeepsDefaultEmotionStyles_WhenPersonaMapEmpty()
    {
        var applied = new AppConfig().ApplyPersona(Manifest(styles: new()));

        Assert.Equal(new AppConfig().EmotionStyleIds, applied.EmotionStyleIds);
    }

    [Fact]
    public void ApplyPersona_MergesBannedWords_KeepingEngineSet()
    {
        var applied = new AppConfig().ApplyPersona(Manifest(bannedWords: new() { "追加ワード", "死ね" }));

        // エンジン共通セットは外せない + ペルソナ固有分が追加される (重複は1つに)
        Assert.Equal(new[] { "死ね", "殺す", "http://", "@", "追加ワード" }, applied.BannedWords);
    }
}
