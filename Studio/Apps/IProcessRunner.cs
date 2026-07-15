namespace Medoz.Studio.Apps;

/// <summary>
/// 外部アプリ (VOICEVOX / PuruPuruPNGTuber) のプロセス操作と死活確認を抽象化する。
/// docs/studio-architecture.md の設計方針「判定ロジックはプロセス操作を IProcessRunner で注入して
/// ユニットテスト対象にする」(TwitchCommentSource.ParseLine と同じ純粋部分の切り出し) に従う。
/// 本番実装は <see cref="ProcessRunner"/>、テストはフェイクを注入する。
/// </summary>
public interface IProcessRunner
{
    /// <summary>exe を起動し、プロセス識別子を返す。起動失敗時は例外を投げる。</summary>
    int Start(string exePath);

    /// <summary>指定 PID のプロセスが生存しているか。</summary>
    bool IsAlive(int processId);

    /// <summary>指定 PID のプロセスを終了する (Studio が起動したプロセスのみ呼ぶ)。</summary>
    void Kill(int processId);

    /// <summary>プロセス名 (拡張子なし) 一致の外部プロセスが存在するか。</summary>
    bool IsProcessRunningByName(string processName);

    /// <summary>
    /// HTTP GET でバージョン文字列を取得する (VOICEVOX の <c>/version</c> 死活確認)。
    /// 応答があれば本文 (バージョン)、応答が無ければ null を返す。
    /// </summary>
    Task<string?> TryGetVersionAsync(string url, CancellationToken ct);

    /// <summary>ポーリング用の待機。テストからは呼び出し回数を数えられるようにする。</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}
