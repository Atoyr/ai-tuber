using Medoz.TwitterBot;

namespace Medoz.TwitterBot.Tests;

public class TweetSchedulerTests
{
    [Theory]
    [InlineData(9, true)]   // 開始時刻ちょうどは含む
    [InlineData(14, true)]
    [InlineData(23, true)]
    [InlineData(8, false)]  // 開始前
    [InlineData(24, false)] // 終了時刻は含まない (0時扱い)
    [InlineData(0, false)]
    public void InActiveHours_9To24(int hour, bool expected)
    {
        Assert.Equal(expected, TweetScheduler.InActiveHours(hour, 9, 24));
    }

    // --scheduled (systemd timer 起動) が使う DateTime オーバーロード。時刻の Hour だけで判定する
    [Theory]
    [InlineData("2026-07-13T09:00:00", true)]  // 開始時刻ちょうど
    [InlineData("2026-07-13T23:59:59", true)]  // 終了直前
    [InlineData("2026-07-13T08:59:59", false)] // 開始前
    [InlineData("2026-07-14T00:00:00", false)] // 深夜 (24時 = 0時扱い)
    [InlineData("2026-07-13T03:30:00", false)]
    public void InActiveHours_DateTime_9To24(string dateTime, bool expected)
    {
        var now = DateTime.Parse(dateTime);
        Assert.Equal(expected, TweetScheduler.InActiveHours(now, 9, 24));
    }

    [Fact]
    public void NextIntervalMinutes_WithinRangeInclusive()
    {
        var random = new Random(12345);
        for (int i = 0; i < 1000; i++)
        {
            int minutes = TweetScheduler.NextIntervalMinutes(random, 180, 360);
            Assert.InRange(minutes, 180, 360);
        }
    }

    [Fact]
    public void NextIntervalMinutes_CanReturnBothBounds()
    {
        var random = new Random(1);
        var seen = new HashSet<int>();
        for (int i = 0; i < 100000; i++)
        {
            seen.Add(TweetScheduler.NextIntervalMinutes(random, 1, 3));
        }
        Assert.Contains(1, seen);
        Assert.Contains(3, seen); // 上端も出る (両端含む)
    }
}
