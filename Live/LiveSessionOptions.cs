namespace Medoz.Live;

/// <summary>
/// 配信ループの実行中に変更可能なパラメータ (docs/studio-architecture.md)。
/// Studio の設定パネルからの即時反映 (<see cref="LiveSession.UpdateOptions"/>) と、
/// CLI 起動時の初期値設定の両方に使う。
/// 秒数は <see cref="double"/> で持ち、UI からは秒単位の数値で扱える。
/// (テストからは 0.05 秒等の短い値を渡してループを高速に回せる。)
/// </summary>
public sealed class LiveSessionOptions
{
    /// <summary>コメントをまとめて拾う間隔 (秒)。既定 4。</summary>
    public double CommentBatchSec { get; set; } = 4;

    /// <summary>この秒数コメントが無ければ自動フリートークする。既定 45。</summary>
    public double FreetalkAfterSec { get; set; } = 45;

    /// <summary>自動フリートークを行うか。false なら無コメントでも発話しない。</summary>
    public bool FreetalkEnabled { get; set; } = true;

    /// <summary>VOICEVOX 話者ID (感情タグ無し・非ストリーミング時の既定スタイル)。</summary>
    public int SpeakerId { get; set; } = 3;

    /// <summary>感情タグ → VOICEVOX スタイルID のマッピング。</summary>
    public IReadOnlyDictionary<string, int> EmotionStyleIds { get; set; } = new Dictionary<string, int>();

    /// <summary>
    /// 一時停止。true の間は応答生成もフリートークも行わず、コメントも Drain せず溜めておく
    /// (再開時に溜まったコメントを処理する)。「合成はするが再生しない」ではなく応答生成自体を止める。
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>コメントバッチ間隔を <see cref="TimeSpan"/> にしたもの (待機に使う)。</summary>
    public TimeSpan CommentBatchInterval => TimeSpan.FromSeconds(CommentBatchSec);

    /// <summary>ループ内で参照するためのスナップショットを作る (ミューテーションと分離する)。</summary>
    public LiveSessionOptions Clone() => new()
    {
        CommentBatchSec = CommentBatchSec,
        FreetalkAfterSec = FreetalkAfterSec,
        FreetalkEnabled = FreetalkEnabled,
        SpeakerId = SpeakerId,
        EmotionStyleIds = EmotionStyleIds,
        Paused = Paused,
    };
}
