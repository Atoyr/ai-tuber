using Medoz.AiTuber.Core;
using Medoz.Live;
using Medoz.MultiLLMClient;
using Medoz.Studio.Settings;
using Medoz.Voicevox;

namespace Medoz.Studio.LiveHosting;

/// <summary>start 失敗など、HTTP でそのままメッセージを返すべきエラー。</summary>
public sealed class LiveSessionHostException : Exception
{
    /// <summary>HTTP ステータスコード (400: 設定不備、409: 状態競合)。</summary>
    public int StatusCode { get; }

    public LiveSessionHostException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>GET /api/status の live セクションに使う情報。</summary>
public sealed record LiveStatus(string State, string? Source, string? PersonaName, DateTimeOffset? StartedAt);

/// <summary>
/// Studio 内で配信セッション (LiveSession) を管理するサービス (docs/studio-architecture.md)。
/// - start: 実効設定 → PersonaPackage / LLM / VOICEVOX / AudioPlayer を構築し、
///   「選択ソース + ManualCommentSource」の合成でバックグラウンド実行
/// - stop: キャンセル → RunAsync 完了待ち (配信メモ保存まで) → 後始末
/// - comment: ManualCommentSource へ注入
/// - 即時反映系の設定変更は LiveSession.UpdateOptions へ
/// 起動失敗 (ペルソナ不足・APIキー未設定・デバイス無し等) は例外メッセージをそのまま返す (fail fast の UX)。
/// </summary>
public sealed class LiveSessionHost : IAsyncDisposable
{
    private readonly StudioSettingsStore _settings;
    private readonly Action<LiveEvent> _onLiveEvent;
    private readonly object _lock = new();

    private LiveSession? _session;
    private ManualCommentSource? _manualSource;
    private AudioPlayer? _audioPlayer;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private string? _source;
    private DateTimeOffset? _startedAt;
    private bool _faulted;

    // 最後にロードできたペルソナ名 (セッション停止中も /api/status で表示する)
    private string? _personaName;

    public LiveSessionHost(StudioSettingsStore settings, Action<LiveEvent> onLiveEvent)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _onLiveEvent = onLiveEvent ?? throw new ArgumentNullException(nameof(onLiveEvent));
    }

    /// <summary>現在の実効設定 (エンジンデフォルト &lt; persona.json &lt; 環境変数 &lt; studio.json)。</summary>
    public EffectiveSettings EffectiveSettings()
    {
        var config = AppConfig.LoadFromEnvironment();
        var merged = SettingsMerger.Merge(config, _settings.Current());
        // PersonaDir は studio.json が勝つので、その値でペルソナを適用し直す
        try
        {
            var package = PersonaPackage.Load(merged.PersonaDir);
            _personaName = package.Manifest.Name;
            config = (config with { PersonaDir = merged.PersonaDir }).ApplyPersona(package.Manifest);
            merged = SettingsMerger.Merge(config, _settings.Current());
        }
        catch (PersonaLoadException)
        {
            // ペルソナがロードできなくても設定表示は返す (persona 由来の項目はエンジンデフォルト)
        }
        return merged;
    }

    /// <summary>現在のセッション状態。</summary>
    public LiveStatus Status()
    {
        lock (_lock)
        {
            string state = _session?.State.ToString() ?? (_faulted ? nameof(SessionState.Faulted) : nameof(SessionState.Stopped));
            return new LiveStatus(state, _source, PersonaNameOrNull(), _startedAt);
        }
    }

    private string? PersonaNameOrNull()
    {
        if (_personaName is not null)
        {
            return _personaName;
        }
        // 未ロードならロードを試み、失敗したら null (フィールド省略)
        try
        {
            var merged = SettingsMerger.Merge(AppConfig.LoadFromEnvironment(), _settings.Current());
            _personaName = PersonaPackage.Load(merged.PersonaDir).Manifest.Name;
        }
        catch (PersonaLoadException)
        {
            return null;
        }
        return _personaName;
    }

    /// <summary>配信セッションを開始する。二重 start は 409。設定不備は 400。</summary>
    public LiveStatus Start(string source, string? target)
    {
        lock (_lock)
        {
            if (_session is not null)
            {
                throw new LiveSessionHostException(409,
                    $"配信セッションは既に {_session.State} です。先に停止してください。");
            }

            // --- 実効設定の解決 (デフォルト < persona.json < 環境変数 < studio.json) ---
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

            // --- ペルソナ (fail fast。不足ファイル名を含むメッセージをそのまま返す) ---
            PersonaPackage personaPackage;
            try
            {
                personaPackage = PersonaPackage.Load(config.PersonaDir, "live_system.md");
            }
            catch (PersonaLoadException ex)
            {
                throw new LiveSessionHostException(400, ex.Message);
            }
            config = config.ApplyPersona(personaPackage.Manifest);
            _personaName = personaPackage.Manifest.Name;
            var effective = SettingsMerger.Merge(config, studio);

            // --- コメントソース: 選択ソース + ManualCommentSource の合成 (manual は Manual のみ) ---
            var manualSource = new ManualCommentSource();
            ICommentSource commentSource;
            try
            {
                commentSource = source.ToLowerInvariant() switch
                {
                    "manual" => manualSource,
                    "twitch" => new CompositeCommentSource(CreateTwitchSource(target), manualSource),
                    "youtube" => new CompositeCommentSource(CreateYouTubeSource(target, config), manualSource),
                    _ => throw new LiveSessionHostException(400,
                        $"未知のコメントソースです: {source} (manual / twitch / youtube)"),
                };
            }
            catch (LiveSessionHostException)
            {
                manualSource.Dispose();
                throw;
            }

            // --- LLM / フィルタ / メモリ / VOICEVOX / 出力デバイス ---
            IChatClient chatClient = LLMClientFactory.CreateChatClient(
                config.LlmProvider, config.LlmApiKey, effective.LlmModel);
            var persona = new Persona(chatClient, personaPackage, "live_system.md");
            var filter = new ModerationFilter(config.BannedWords);
            var memory = new SharedMemory(config.MemoryPathFor(personaPackage.Manifest.Slug), config.RecentTweetsKeep);
            var voicevox = new VoicevoxClient(config.VoicevoxUrl);

            AudioPlayer audioPlayer;
            try
            {
                audioPlayer = new AudioPlayer(effective.OutputDevice); // 部分一致。無ければ候補一覧付き例外
            }
            catch (InvalidOperationException ex)
            {
                commentSource.Dispose();
                throw new LiveSessionHostException(400, ex.Message);
            }

            var options = new LiveSessionOptions
            {
                CommentBatchSec = effective.CommentBatchSec,
                FreetalkAfterSec = effective.FreetalkAfterSec,
                FreetalkEnabled = effective.FreetalkEnabled,
                SpeakerId = effective.SpeakerId,
                EmotionStyleIds = config.EmotionStyleIds,
                Paused = effective.Paused,
            };

            var session = new LiveSession(
                options, commentSource, persona, filter, memory, voicevox, audioPlayer,
                config.HistoryTurns, config.UseStreaming);
            session.EventRaised += _onLiveEvent;

            var cts = new CancellationTokenSource();
            _session = session;
            _manualSource = manualSource;
            _audioPlayer = audioPlayer;
            _cts = cts;
            _source = source.ToLowerInvariant();
            _startedAt = DateTimeOffset.Now;
            _faulted = false;

            // バックグラウンドで配信ループを実行。致命例外は Faulted 表示に使う (握って再スローしない)
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await session.RunAsync(cts.Token);
                }
                catch
                {
                    // LiveSession が Faulted へ遷移済み。SessionStateChanged が SSE で通知される
                }
            });

            return new LiveStatus(nameof(SessionState.Starting), _source, _personaName, _startedAt);
        }
    }

    private static ICommentSource CreateTwitchSource(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new LiveSessionHostException(400, "twitch ソースには target (チャンネル名または twitch.tv の URL) が必要です。");
        }
        return new TwitchCommentSource(target);
    }

    private static ICommentSource CreateYouTubeSource(string? target, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new LiveSessionHostException(400, "youtube ソースには target (videoId または URL) が必要です。");
        }
        if (string.IsNullOrEmpty(config.YouTubeApiKey))
        {
            throw new LiveSessionHostException(400, "環境変数 YOUTUBE_API_KEY が未設定です (YouTube Data API v3 のキーが必要です)。");
        }
        return new YouTubeCommentSource(target, config.YouTubeApiKey);
    }

    /// <summary>配信セッションを停止する (配信メモ保存まで待つ)。</summary>
    public async Task<LiveStatus> StopAsync()
    {
        LiveSession? session;
        CancellationTokenSource? cts;
        Task? runTask;
        lock (_lock)
        {
            if (_session is null)
            {
                throw new LiveSessionHostException(409, "配信セッションは開始されていません。");
            }
            session = _session;
            cts = _cts;
            runTask = _runTask;
        }

        cts!.Cancel();
        try
        {
            if (runTask is not null)
            {
                await runTask; // 配信メモ保存 (Stopping → Stopped) まで待つ。致命例外は Task.Run 内で握り済み
            }
        }
        finally
        {
            bool faulted = session!.State == SessionState.Faulted;
            await session.DisposeAsync(); // コメントソース (Manual 含む) はここで破棄される
            lock (_lock)
            {
                _audioPlayer?.Dispose(); // Studio が作った出力デバイスの後始末
                _audioPlayer = null;
                _session = null;
                _manualSource = null;
                _cts = null;
                _runTask = null;
                _source = null;
                _startedAt = null;
                _faulted = faulted;
            }
            cts.Dispose();
        }
        return Status();
    }

    /// <summary>UI からのテストコメント注入。</summary>
    public void InjectComment(string author, string text)
    {
        ManualCommentSource? manual;
        lock (_lock)
        {
            manual = _manualSource;
        }
        if (manual is null)
        {
            throw new LiveSessionHostException(409, "配信セッションが開始されていないため、コメントを注入できません。");
        }
        manual.Enqueue(new Comment(author, text));
    }

    /// <summary>実行中セッションがあれば即時反映系の設定を反映する (無ければ何もしない)。</summary>
    public void ApplyImmediate(ImmediatePatch patch)
    {
        LiveSession? session;
        lock (_lock)
        {
            session = _session;
        }
        session?.UpdateOptions(options =>
        {
            if (patch.CommentBatchSec is double batch) { options.CommentBatchSec = batch; }
            if (patch.FreetalkAfterSec is double freetalk) { options.FreetalkAfterSec = freetalk; }
            if (patch.FreetalkEnabled is bool enabled) { options.FreetalkEnabled = enabled; }
            if (patch.SpeakerId is int speakerId) { options.SpeakerId = speakerId; }
            if (patch.Paused is bool paused) { options.Paused = paused; }
        });
    }

    public async ValueTask DisposeAsync()
    {
        bool running;
        lock (_lock)
        {
            running = _session is not null;
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
