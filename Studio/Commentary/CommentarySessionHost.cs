using Medoz.AiTuber.Core;
using Medoz.GameCommentary;
using Medoz.MultiLLMClient;
using Medoz.Studio.LiveHosting;
using Medoz.Studio.Settings;
using Medoz.Voicevox;

namespace Medoz.Studio.Commentary;

/// <summary>UI・ログへ流すゲーム実況イベント (LiveEvent と同じ方針の UI 非依存 record)。</summary>
public abstract record CommentaryEvent(DateTimeOffset At);

/// <summary>実況が生成・発話された。</summary>
public sealed record CommentarySpoken(DateTimeOffset At, string Text) : CommentaryEvent(At);

/// <summary>CommentaryLoop の診断ログ ([error] / [skip])。</summary>
public sealed record CommentaryLogMessage(DateTimeOffset At, string Level, string Message) : CommentaryEvent(At);

/// <summary>実況セッションの状態が変わった (Stopped / Running / Faulted)。</summary>
public sealed record CommentaryStateChanged(DateTimeOffset At, string State) : CommentaryEvent(At);

/// <summary>GET /api/status の commentary セクションに使う情報。</summary>
public sealed record CommentaryStatus(string State, string? Window, DateTimeOffset? StartedAt);

/// <summary>
/// Studio 内でゲーム実況セッション (GameCommentary の CommentaryLoop) を管理するサービス。
/// - start: 実効設定 → PersonaPackage (game_system.md) / LLM / WindowCapture / VOICEVOX を構築し、
///   CAPTURE_INTERVAL_SEC 間隔のキャプチャ→Vision実況→発話ループをバックグラウンド実行
/// - stop: キャンセル → ループ完了待ち → 後始末
/// 起動失敗 (ウィンドウ未発見・APIキー未設定・デバイス無し等) は例外メッセージをそのまま返す
/// (LiveSessionHost と同じ fail fast の UX。ウィンドウ未発見時は候補一覧がメッセージに含まれる)。
/// </summary>
public sealed class CommentarySessionHost : IAsyncDisposable
{
    private readonly StudioSettingsStore _settings;
    private readonly Action<CommentaryEvent> _onEvent;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private VoicevoxSpeaker? _speaker;
    private string? _window;
    private DateTimeOffset? _startedAt;
    private bool _faulted;

    public CommentarySessionHost(StudioSettingsStore settings, Action<CommentaryEvent> onEvent)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
    }

    /// <summary>現在の実況セッション状態。</summary>
    public CommentaryStatus Status()
    {
        lock (_lock)
        {
            string state = _runTask is not null ? "Running" : (_faulted ? "Faulted" : "Stopped");
            return new CommentaryStatus(state, _window, _startedAt);
        }
    }

    /// <summary>実況セッションを開始する。二重 start は 409。設定不備は 400。</summary>
    public CommentaryStatus Start(string? windowTitle)
    {
        lock (_lock)
        {
            if (_runTask is not null)
            {
                throw new LiveSessionHostException(409,
                    $"ゲーム実況は既に実行中です (対象: {_window})。先に停止してください。");
            }

            // --- 実効設定の解決 (デフォルト < persona.json < 環境変数 < studio.json。Live と同じ) ---
            var config = AppConfig.LoadFromEnvironment();
            var studio = _settings.Current();
            var preMerge = SettingsMerger.Merge(config, studio);
            config = config with { PersonaDir = preMerge.PersonaDir, LlmProvider = preMerge.LlmProvider };

            if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLowerInvariant()))
            {
                throw new LiveSessionHostException(400,
                    $"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai)");
            }
            if (string.IsNullOrEmpty(config.LlmApiKey))
            {
                throw new LiveSessionHostException(400,
                    $"環境変数 {config.LlmApiKeyEnvName} が未設定です (LLM_PROVIDER={config.LlmProvider})");
            }

            // ウィンドウ: リクエスト → 環境変数 WINDOW_TITLE_FRAGMENT の順 (CLI の解決順と同じ)
            string window = !string.IsNullOrWhiteSpace(windowTitle) ? windowTitle.Trim() : config.GameWindowTitle;
            if (string.IsNullOrEmpty(window))
            {
                throw new LiveSessionHostException(400,
                    "window (対象ウィンドウタイトルの一部) を指定してください。候補は GET /api/windows で取得できます。");
            }

            // --- ペルソナ (実況モードは game_system.md。不足ファイル名を含むメッセージをそのまま返す) ---
            PersonaPackage personaPackage;
            try
            {
                personaPackage = PersonaPackage.Load(config.PersonaDir, "game_system.md");
            }
            catch (PersonaLoadException ex)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }
            config = config.ApplyPersona(personaPackage.Manifest);
            var effective = SettingsMerger.Merge(config, studio);

            // --- 対象ウィンドウ (見つからなければ可視ウィンドウ一覧付き例外をそのまま返す) ---
            IWindowCapture capture;
            try
            {
                capture = new WindowCapture(window, config.MaxImageWidth);
            }
            catch (InvalidOperationException ex)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }

            // --- LLM / フィルタ / VOICEVOX / 出力デバイス ---
            IChatClient chatClient = LLMClientFactory.CreateChatClient(
                config.LlmProvider, config.LlmApiKey, effective.LlmModel);
            var persona = new Persona(chatClient, personaPackage, "game_system.md");
            var filter = new ModerationFilter(config.BannedWords);
            var voicevox = new VoicevoxClient(config.VoicevoxUrl);

            AudioPlayer audioPlayer;
            try
            {
                audioPlayer = new AudioPlayer(effective.OutputDevice); // 部分一致。無ければ候補一覧付き例外
            }
            catch (InvalidOperationException ex)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }

            var styleMap = new EmotionStyleMap(effective.SpeakerId, config.EmotionStyleIds);
            var speaker = new VoicevoxSpeaker(voicevox, audioPlayer, effective.SpeakerId, styleMap);

            var loop = new CommentaryLoop(
                capture, persona, filter, speaker,
                config.CommentaryHistoryLimit, personaPackage.Manifest.Name,
                log: message => _onEvent(new CommentaryLogMessage(
                    DateTimeOffset.Now,
                    message.StartsWith("[error]", StringComparison.Ordinal) ? "error" : "info",
                    message)));

            var cts = new CancellationTokenSource();
            _cts = cts;
            _speaker = speaker;
            _window = capture.TargetTitle;
            _startedAt = DateTimeOffset.Now;
            _faulted = false;
            _runTask = Task.Run(() => RunLoopAsync(loop, config.CaptureIntervalSec, cts.Token));

            _onEvent(new CommentaryStateChanged(DateTimeOffset.Now, "Running"));
            return new CommentaryStatus("Running", _window, _startedAt);
        }
    }

    /// <summary>CAPTURE_INTERVAL_SEC 間隔で実況を回す (実処理時間を差し引く。CLI と同じ)。</summary>
    private async Task RunLoopAsync(CommentaryLoop loop, int intervalSec, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;

                string? comment = await loop.RunOnceAsync(ct);
                if (comment is not null)
                {
                    _onEvent(new CommentarySpoken(DateTimeOffset.Now, comment));
                }

                double elapsed = (DateTime.UtcNow - start).TotalSeconds;
                double wait = Math.Max(0, intervalSec - elapsed);
                if (wait > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stop による正常終了
        }
        catch (Exception ex)
        {
            // 想定外の致命エラー: Faulted 表示にしてループを終える (RunOnceAsync 内のエラーはスキップ済み)
            lock (_lock)
            {
                _faulted = true;
            }
            _onEvent(new CommentaryLogMessage(DateTimeOffset.Now, "error", $"[error] 実況ループが停止しました: {ex.Message}"));
        }
    }

    /// <summary>実況セッションを停止する (ループ完了と後始末まで待つ)。</summary>
    public async Task<CommentaryStatus> StopAsync()
    {
        CancellationTokenSource? cts;
        Task? runTask;
        VoicevoxSpeaker? speaker;
        lock (_lock)
        {
            if (_runTask is null)
            {
                throw new LiveSessionHostException(409, "ゲーム実況は開始されていません。");
            }
            cts = _cts;
            runTask = _runTask;
            speaker = _speaker;
        }

        cts!.Cancel();
        try
        {
            await runTask!; // 致命例外は RunLoopAsync 内で握り済み
        }
        finally
        {
            speaker?.Dispose(); // AudioPlayer もここで破棄される
            lock (_lock)
            {
                _cts = null;
                _runTask = null;
                _speaker = null;
                _window = null;
                _startedAt = null;
            }
            cts.Dispose();
            _onEvent(new CommentaryStateChanged(DateTimeOffset.Now, Status().State));
        }
        return Status();
    }

    public async ValueTask DisposeAsync()
    {
        bool running;
        lock (_lock)
        {
            running = _runTask is not null;
        }
        if (running)
        {
            try
            {
                await StopAsync();
            }
            catch (LiveSessionHostException)
            {
                // 停止済みなら何もしない
            }
        }
    }
}
