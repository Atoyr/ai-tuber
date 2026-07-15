namespace Medoz.Live;

/// <summary>配信セッションの状態。UI・CLI から状態バッジ表示に使う。</summary>
public enum SessionState
{
    /// <summary>コメントソースの起動中 (RunAsync の入り口)</summary>
    Starting,

    /// <summary>配信ループ稼働中</summary>
    Running,

    /// <summary>停止処理中 (キャンセル受理後・配信メモ保存中)</summary>
    Stopping,

    /// <summary>正常停止</summary>
    Stopped,

    /// <summary>予期しない例外で停止</summary>
    Faulted,
}

/// <summary>
/// 配信ループが発火する UI 非依存のイベント (docs/studio-architecture.md の定義)。
/// CLI は Console.WriteLine に、Studio は SSE に流す。
/// </summary>
public abstract record LiveEvent(DateTimeOffset At);

/// <summary>この回で応答対象に選ばれたコメント群。</summary>
public record CommentsPicked(DateTimeOffset At, IReadOnlyList<Comment> Comments) : LiveEvent(At);

/// <summary>コメントが途切れ、自動フリートークを開始した。</summary>
public record FreeTalkTriggered(DateTimeOffset At) : LiveEvent(At);

/// <summary>応答を発話した (発話済みの本文)。</summary>
public record ReplySpoken(DateTimeOffset At, string Text) : LiveEvent(At);

/// <summary>応答を破棄した (フィルタ違反・発話内容なし等)。<see cref="Reason"/> は理由の文言。</summary>
public record ReplySkipped(DateTimeOffset At, string Reason) : LiveEvent(At);

/// <summary>セッション状態が遷移した。</summary>
public record SessionStateChanged(DateTimeOffset At, SessionState State) : LiveEvent(At);

/// <summary>配信終了時に配信メモ (発話ログ要約) を保存した。</summary>
public record StreamNoteSaved(DateTimeOffset At, string Summary) : LiveEvent(At);
