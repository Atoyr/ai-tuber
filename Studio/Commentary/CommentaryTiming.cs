namespace Medoz.Studio.Commentary;

/// <summary>
/// 実況ループの間隔方式 (Studio 設定 commentaryTimingMode)。
/// - <see cref="Interval"/>: キャプチャ開始から一定間隔。実処理時間を差し引く従来挙動 (CAPTURE_INTERVAL_SEC)
/// - <see cref="AfterSpeech"/>: 発話が終わってから一定秒数 (commentaryAfterSpeechSec) 待って次のキャプチャへ
/// 待ち時間の計算は純粋関数に分離してユニットテスト対象にする (SettingsMerger と同じ方針)。
/// </summary>
public static class CommentaryTiming
{
    public const string Interval = "interval";
    public const string AfterSpeech = "afterSpeech";

    /// <summary>afterSpeech 方式の既定の待ち秒数 (Studio 固有設定のためここが既定)。</summary>
    public const double DefaultAfterSpeechSec = 5;

    /// <summary>方式名を正規化する。未知の値は従来挙動 (interval) に倒す。</summary>
    public static string Normalize(string? mode)
        => string.Equals(mode, AfterSpeech, StringComparison.OrdinalIgnoreCase) ? AfterSpeech : Interval;

    /// <summary>
    /// 次のキャプチャまでの待ち秒数を返す。
    /// interval: intervalSec から実処理時間 elapsedSec を差し引く (下限0)。
    /// afterSpeech: 実処理時間によらず afterSpeechSec (下限0)。
    /// </summary>
    public static double NextWaitSec(string? mode, double intervalSec, double afterSpeechSec, double elapsedSec)
        => Normalize(mode) == AfterSpeech
            ? Math.Max(0, afterSpeechSec)
            : Math.Max(0, intervalSec - elapsedSec);
}
