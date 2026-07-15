namespace Medoz.Studio.Apps;

/// <summary>
/// 外部アプリ (VOICEVOX / PuruPuruPNGTuber) の状態機械 (docs/studio-architecture.md)。
/// </summary>
public enum AppState
{
    /// <summary>exe パス未設定。起動・停止ボタンとも無効。</summary>
    NotConfigured,

    /// <summary>設定済みだが停止中。</summary>
    Stopped,

    /// <summary>起動処理中 (VOICEVOX は /version 応答待ち)。</summary>
    Starting,

    /// <summary>Studio が起動して稼働中。</summary>
    Running,

    /// <summary>外部 (ユーザーが手起動) で稼働中。Studio は停止させない。</summary>
    RunningExternal,

    /// <summary>起動失敗・タイムアウト等の異常。</summary>
    Faulted,
}

/// <summary>アプリの現在状態 (VOICEVOX はバージョンも持つ)。</summary>
public sealed record AppStatus(AppState State, string? Version = null);

/// <summary>死活確認の方式。</summary>
public enum LivenessMode
{
    /// <summary>HTTP GET /version の応答で判定 (VOICEVOX)。</summary>
    Http,

    /// <summary>プロセスの生存で判定 (PuruPuruPNGTuber)。</summary>
    Process,
}

/// <summary>1つの管理対象アプリの設定。</summary>
public sealed record ManagedAppConfig
{
    /// <summary>表示名 (ログ・例外メッセージ用)。</summary>
    public required string DisplayName { get; init; }

    /// <summary>現在の exe パスを解決する (環境変数など。未設定なら null/空)。</summary>
    public required Func<string?> ExePathProvider { get; init; }

    public required LivenessMode LivenessMode { get; init; }

    /// <summary>HTTP 死活確認の URL (<see cref="LivenessMode.Http"/> のとき必須)。</summary>
    public string? HealthUrl { get; init; }

    /// <summary>外部プロセス検出に使うプロセス名 (拡張子なし)。</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>起動待ちのタイムアウト秒数。既定 60。</summary>
    public int StartTimeoutSec { get; init; } = 60;

    /// <summary>死活ポーリングの間隔秒数。既定 2。</summary>
    public int PollIntervalSec { get; init; } = 2;
}
