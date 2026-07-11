using Medoz.AiTuber.Core;
using Medoz.TwitterBot;

namespace Medoz.TwitterBot.Tests;

public class TweetMessagesTests
{
    private static readonly DateTime Wednesday = new(2026, 7, 8, 14, 30, 0); // 2026-07-08 は水曜日

    [Fact]
    public void BuildContext_IncludesDateTimeWithWeekday()
    {
        var memory = new MemoryData();

        string context = TweetMessages.BuildContext(memory, Wednesday);

        Assert.Contains("現在日時: 2026-07-08 14:30 (水曜日)", context);
        Assert.EndsWith("この状況に合うツイートを1つ、指示されたJSON形式で生成してください。", context);
    }

    [Fact]
    public void BuildContext_OmitsStreamNoteAndTweets_WhenEmpty()
    {
        string context = TweetMessages.BuildContext(new MemoryData(), Wednesday);

        Assert.DoesNotContain("直近の配信メモ", context);
        Assert.DoesNotContain("自分の直近ツイート", context);
    }

    [Fact]
    public void BuildContext_IncludesLatestStreamNote()
    {
        var memory = new MemoryData
        {
            StreamNotes =
            {
                new StreamNote("2026-07-01 20:00", "古い配信"),
                new StreamNote("2026-07-07 21:00", "レトロゲームを遊んだ"),
            },
        };

        string context = TweetMessages.BuildContext(memory, Wednesday);

        Assert.Contains("直近の配信メモ (2026-07-07 21:00): レトロゲームを遊んだ", context);
        Assert.DoesNotContain("古い配信", context); // 末尾(最新)の1件だけ
    }

    [Fact]
    public void BuildContext_IncludesUpToTenRecentTweets()
    {
        var memory = new MemoryData();
        for (int i = 1; i <= 12; i++)
        {
            memory.RecentTweets.Add($"tweet{i}");
        }

        string context = TweetMessages.BuildContext(memory, Wednesday);

        Assert.Contains("自分の直近ツイート(繰り返し禁止):", context);
        Assert.Contains("- tweet1", context);
        Assert.Contains("- tweet10", context);
        Assert.DoesNotContain("- tweet11", context); // 先頭10件のみ
    }

    [Theory]
    [InlineData("{\"tweet\": \"こんにちは\", \"kind\": \"daily\"}", "こんにちは")]
    [InlineData("```json\n{\"tweet\": \"フェンス付き\", \"kind\": \"daily\"}\n```", "フェンス付き")]
    [InlineData("```\n{\"tweet\": \"素のフェンス\", \"kind\": \"daily\"}\n```", "素のフェンス")]
    [InlineData("{\"tweet\": \"  前後空白  \", \"kind\": \"daily\"}", "前後空白")]
    public void ParseTweet_ExtractsTweetText(string raw, string expected)
    {
        Assert.Equal(expected, TweetMessages.ParseTweet(raw));
    }

    [Theory]
    [InlineData("これはJSONではない")]
    [InlineData("{\"kind\": \"daily\"}")]              // tweet キーなし
    [InlineData("{\"tweet\": 123, \"kind\": \"daily\"}")] // tweet が文字列でない
    [InlineData("{\"tweet\":")]                        // 壊れたJSON
    public void ParseTweet_ReturnsNull_OnInvalidInput(string raw)
    {
        Assert.Null(TweetMessages.ParseTweet(raw));
    }

    [Fact]
    public void CountLength_CountsEmojiAsOneCodePoint()
    {
        // Python len("あ😀い") == 3 (コードポイント数)
        Assert.Equal(3, TweetMessages.CountLength("あ😀い"));
    }

    [Fact]
    public void CountLength_CountsAsciiAndJapanese()
    {
        Assert.Equal(5, TweetMessages.CountLength("hello"));
        Assert.Equal(3, TweetMessages.CountLength("あいう"));
    }
}
