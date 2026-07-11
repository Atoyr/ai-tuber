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
        Assert.Equal(new[] { "死ね", "殺す", "http://", "@" }, config.BannedWords);
        Assert.Equal(Path.Combine("data", "memory.json"), config.MemoryPath);
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
}
