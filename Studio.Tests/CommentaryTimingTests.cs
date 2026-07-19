using Medoz.Studio.Commentary;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// 実況ループの待ち時間計算 (docs/studio-architecture.md のゲーム実況パネル)。
/// interval = キャプチャ開始から一定間隔 (実処理時間を差し引く従来挙動)、
/// afterSpeech = 発話が終わってから一定秒数。
/// </summary>
public class CommentaryTimingTests
{
    [Fact]
    public void Interval方式は実処理時間を差し引く()
    {
        Assert.Equal(9, CommentaryTiming.NextWaitSec(CommentaryTiming.Interval,
            intervalSec: 12, afterSpeechSec: 5, elapsedSec: 3));
    }

    [Fact]
    public void Interval方式で実処理時間が間隔を超えたら待たない()
    {
        Assert.Equal(0, CommentaryTiming.NextWaitSec(CommentaryTiming.Interval,
            intervalSec: 12, afterSpeechSec: 5, elapsedSec: 20));
    }

    [Fact]
    public void AfterSpeech方式は実処理時間によらず一定秒数待つ()
    {
        // RunOnceAsync は発話完了まで待つので、この待ちは「発話が終わってから」の秒数になる
        Assert.Equal(5, CommentaryTiming.NextWaitSec(CommentaryTiming.AfterSpeech,
            intervalSec: 12, afterSpeechSec: 5, elapsedSec: 30));
        Assert.Equal(5, CommentaryTiming.NextWaitSec(CommentaryTiming.AfterSpeech,
            intervalSec: 12, afterSpeechSec: 5, elapsedSec: 0));
    }

    [Fact]
    public void 負の秒数は0に丸める()
    {
        Assert.Equal(0, CommentaryTiming.NextWaitSec(CommentaryTiming.AfterSpeech,
            intervalSec: 12, afterSpeechSec: -1, elapsedSec: 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("interval")]
    public void 未知の方式はIntervalに正規化される(string? mode)
    {
        Assert.Equal(CommentaryTiming.Interval, CommentaryTiming.Normalize(mode));
        // 待ち時間も interval として計算される
        Assert.Equal(9, CommentaryTiming.NextWaitSec(mode, intervalSec: 12, afterSpeechSec: 5, elapsedSec: 3));
    }

    [Fact]
    public void AfterSpeechは大文字小文字を無視して正規化される()
    {
        Assert.Equal(CommentaryTiming.AfterSpeech, CommentaryTiming.Normalize("afterspeech"));
        Assert.Equal(CommentaryTiming.AfterSpeech, CommentaryTiming.Normalize("afterSpeech"));
    }
}
