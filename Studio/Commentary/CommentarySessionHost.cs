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
///   キャプチャ→Vision実況→発話ループをバックグラウンド実行。間隔は UI 設定
///   (captureIntervalSec / commentaryTimingMode / commentaryAfterSpeechSec) を毎回読み直して即時反映
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
    private IWindowCapture? _capture;
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

    /// <summary>
    /// 実況セッションを開始する。二重 start は 409。設定不備は 400。
    /// game を指定するとペルソナの knowledge/&lt;game&gt;.md をシステムプロンプトに結合する
    /// (無い知識名なら利用可能一覧付きの 400。未指定なら環境変数 GAME_KNOWLEDGE → 知識なし)。
    /// </summary>
    public CommentaryStatus Start(string? windowTitle, string? game = null)
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

            // --- LLM / ゲーム知識 (キャプチャ生成より前に検証し、失敗時にキャプチャを残さない) ---
            IChatClient chatClient = LLMClientFactory.CreateChatClient(
                config.LlmProvider, config.LlmApiKey, effective.LlmModel);

            // ゲーム知識: リクエスト → 環境変数 GAME_KNOWLEDGE の順 (CLI の --game と同じ解決順)
            string? knowledge = !string.IsNullOrWhiteSpace(game) ? game.Trim()
                : (string.IsNullOrEmpty(config.GameKnowledge) ? null : config.GameKnowledge);
            Persona persona;
            try
            {
                persona = new Persona(chatClient, personaPackage, "game_system.md", knowledge);
            }
            catch (PersonaLoadException ex)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }

            // --- 対象ウィンドウ (見つからなければ可視ウィンドウ一覧付き例外をそのまま返す) ---
            // 方式は CAPTURE_METHOD (既定 wgc)。wgc は管理者権限で動くゲームも撮れる
            IWindowCapture capture;
            try
            {
                capture = WindowCaptureFactory.Create(config.CaptureMethod, window, config.MaxImageWidth);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }

            // --- フィルタ / VOICEVOX / 出力デバイス ---
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
                    message)),
                maxTokens: config.CommentaryMaxTokens,
                // 発話テキストは SSE へ (発話開始時点で会話ログに出る)。既定の Console.WriteLine のままだと
                // Studio のコンソールをクリックしてテキスト選択に入った瞬間ループが凍結し発話が止まる
                onSpeaking: text => _onEvent(new CommentarySpoken(DateTimeOffset.Now, text)));

            var cts = new CancellationTokenSource();
            _cts = cts;
            _speaker = speaker;
            _capture = capture;
            _window = capture.TargetTitle;
            _startedAt = DateTimeOffset.Now;
            _faulted = false;
            _runTask = Task.Run(() => RunLoopAsync(loop, config, cts.Token));

            _onEvent(new CommentaryStateChanged(DateTimeOffset.Now, "Running"));
            return new CommentaryStatus("Running", _window, _startedAt);
        }
    }

    /// <summary>
    /// 実況を回すループ。待ち時間は毎回 studio.json を読み直して計算するため、
    /// キャプチャ間隔・方式 (interval / afterSpeech) の UI 変更が次の待ちから即時反映される。
    /// </summary>
    private async Task RunLoopAsync(CommentaryLoop loop, AppConfig config, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var start = DateTime.UtcNow;

                // 発話テキストの SSE 通知は CommentaryLoop の onSpeaking (発話開始時) が行う
                await loop.RunOnceAsync(ct);

                double elapsed = (DateTime.UtcNow - start).TotalSeconds;
                var effective = SettingsMerger.Merge(config, _settings.Current());
                double wait = CommentaryTiming.NextWaitSec(
                    effective.CommentaryTimingMode, effective.CaptureIntervalSec,
                    effective.CommentaryAfterSpeechSec, elapsed);
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
        IWindowCapture? capture;
        lock (_lock)
        {
            if (_runTask is null)
            {
                throw new LiveSessionHostException(409, "ゲーム実況は開始されていません。");
            }
            cts = _cts;
            runTask = _runTask;
            speaker = _speaker;
            capture = _capture;
        }

        cts!.Cancel();
        try
        {
            await runTask!; // 致命例外は RunLoopAsync 内で握り済み
        }
        finally
        {
            speaker?.Dispose(); // AudioPlayer もここで破棄される
            (capture as IDisposable)?.Dispose(); // WGC は D3D デバイスを持つ
            lock (_lock)
            {
                _cts = null;
                _runTask = null;
                _speaker = null;
                _capture = null;
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
