namespace Medoz.Studio.Apps;

/// <summary>
/// 1つの外部アプリ (VOICEVOX または PuruPuruPNGTuber) の起動・停止・死活監視を担う状態機械。
/// docs/studio-architecture.md の設計原則:
/// - すでに起動していたら起動しない (VOICEVOX は /version 応答、PuruPuru はプロセス名一致で検出 → RunningExternal)
/// - Studio が起動したプロセスのみ Kill する (ユーザーが手起動したものは殺さない)
/// - exe パス未設定なら NotConfigured
///
/// プロセス操作・HTTP 死活確認は <see cref="IProcessRunner"/> で注入するため、状態遷移をユニットテストできる。
/// </summary>
public sealed class ManagedApp
{
    private readonly IProcessRunner _runner;
    private readonly ManagedAppConfig _config;
    private readonly object _lock = new();

    private int? _ownedPid;      // Studio が起動したプロセスの PID (外部起動時は null)
    private AppState _state = AppState.Stopped;
    private string? _version;

    public ManagedApp(IProcessRunner runner, ManagedAppConfig config)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        if (config.LivenessMode == LivenessMode.Http && string.IsNullOrEmpty(config.HealthUrl))
        {
            throw new ArgumentException("LivenessMode.Http では HealthUrl が必須です。", nameof(config));
        }
    }

    public string DisplayName => _config.DisplayName;

    /// <summary>Studio が起動したプロセスを保持しているか (停止ボタンの有効判定に使う)。</summary>
    public bool OwnsProcess
    {
        get { lock (_lock) { return _ownedPid.HasValue; } }
    }

    public AppStatus Current()
    {
        lock (_lock)
        {
            return new AppStatus(_state, _version);
        }
    }

    private bool HasExePath => !string.IsNullOrWhiteSpace(_config.ExePathProvider());

    /// <summary>死活を確認して状態を更新する。起動処理中 (Starting) は乱さない。</summary>
    public async Task<AppStatus> RefreshAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_state == AppState.Starting)
            {
                return new AppStatus(_state, _version);
            }
        }

        if (_config.LivenessMode == LivenessMode.Http)
        {
            string? version = await _runner.TryGetVersionAsync(_config.HealthUrl!, ct);
            lock (_lock)
            {
                if (_state == AppState.Starting)
                {
                    return new AppStatus(_state, _version);
                }
                if (version is not null)
                {
                    _version = version;
                    _state = _ownedPid.HasValue ? AppState.Running : AppState.RunningExternal;
                }
                else
                {
                    _version = null;
                    if (_ownedPid.HasValue)
                    {
                        // Studio が起動したはずのプロセスが応答しない → 死んだとみなす
                        _ownedPid = null;
                        _state = HasExePath ? AppState.Stopped : AppState.NotConfigured;
                    }
                    else
                    {
                        _state = HasExePath ? AppState.Stopped : AppState.NotConfigured;
                    }
                }
                return new AppStatus(_state, _version);
            }
        }

        // LivenessMode.Process
        lock (_lock)
        {
            if (_state == AppState.Starting)
            {
                return new AppStatus(_state, _version);
            }
            if (_ownedPid.HasValue)
            {
                if (_runner.IsAlive(_ownedPid.Value))
                {
                    _state = AppState.Running;
                }
                else
                {
                    _ownedPid = null;
                    _state = HasExePath ? AppState.Stopped : AppState.NotConfigured;
                }
            }
            else if (_runner.IsProcessRunningByName(_config.ProcessName))
            {
                _state = AppState.RunningExternal;
            }
            else
            {
                _state = HasExePath ? AppState.Stopped : AppState.NotConfigured;
            }
            return new AppStatus(_state, _version);
        }
    }

    /// <summary>アプリを起動する。すでに起動済み (自分/外部) なら何もしない。</summary>
    public async Task<AppStatus> StartAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        lock (_lock)
        {
            if (_state is AppState.Running or AppState.RunningExternal)
            {
                // すでに起動している (外部含む) → 起動しない
                return new AppStatus(_state, _version);
            }
        }

        string? exePath = _config.ExePathProvider();
        if (string.IsNullOrWhiteSpace(exePath))
        {
            lock (_lock) { _state = AppState.NotConfigured; }
            throw new InvalidOperationException(
                $"{_config.DisplayName} の exe パスが未設定です。環境変数で exe パスを設定してください。");
        }

        lock (_lock) { _state = AppState.Starting; }

        int pid;
        try
        {
            pid = _runner.Start(exePath);
        }
        catch (Exception ex)
        {
            lock (_lock) { _state = AppState.Faulted; }
            throw new InvalidOperationException($"{_config.DisplayName} の起動に失敗しました: {ex.Message}", ex);
        }

        lock (_lock) { _ownedPid = pid; }

        if (_config.LivenessMode == LivenessMode.Http)
        {
            // /version が応答するまでポーリング (最大 StartTimeoutSec 秒)
            int attempts = Math.Max(1, _config.StartTimeoutSec / Math.Max(1, _config.PollIntervalSec));
            for (int i = 0; i < attempts; i++)
            {
                string? version = await _runner.TryGetVersionAsync(_config.HealthUrl!, ct);
                if (version is not null)
                {
                    lock (_lock)
                    {
                        _version = version;
                        _state = AppState.Running;
                        return new AppStatus(_state, _version);
                    }
                }
                await _runner.DelayAsync(TimeSpan.FromSeconds(_config.PollIntervalSec), ct);
            }
            lock (_lock) { _state = AppState.Faulted; }
            throw new InvalidOperationException(
                $"{_config.DisplayName} が {_config.StartTimeoutSec} 秒以内に起動しませんでした ({_config.HealthUrl})。");
        }

        // LivenessMode.Process: プロセスが生きていれば Running
        lock (_lock)
        {
            _state = _runner.IsAlive(pid) ? AppState.Running : AppState.Faulted;
            return new AppStatus(_state, _version);
        }
    }

    /// <summary>アプリを停止する。Studio が起動したプロセスのみ Kill する (外部起動は殺さない)。</summary>
    public Task<AppStatus> StopAsync()
    {
        lock (_lock)
        {
            if (_ownedPid.HasValue)
            {
                try
                {
                    _runner.Kill(_ownedPid.Value);
                }
                catch
                {
                    // すでに終了している場合など。停止扱いにする
                }
                _ownedPid = null;
                _version = null;
                _state = HasExePath ? AppState.Stopped : AppState.NotConfigured;
            }
            // 外部起動 (RunningExternal) は殺さない = 状態そのまま
            return Task.FromResult(new AppStatus(_state, _version));
        }
    }
}
