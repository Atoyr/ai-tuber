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
    public void LoadFromEnvironment_UsesDryRunDefault_WhenEnvNotSet()
    {
        Environment.SetEnvironmentVariable("TWEET_DRY_RUN", null);

        var config = AppConfig.LoadFromEnvironment();

        Assert.True(config.TweetDryRun);
    }
}
