using System.Text;
using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;
using Medoz.Voicevox;

namespace Medoz.Live;

/// <summary>
/// 配信ループ本体 (docs/studio-architecture.md)。
/// これまで Live/Program.cs に in-line で書かれていたループを抽出したもので、
/// CLI (Program.cs) と Studio (Web UI) の両方から同じループを使う。
///
/// 挙動は Program.cs 時代と同一:
/// - <see cref="LiveSessionOptions.CommentBatchSec"/> 秒バッチでコメントを拾う
/// - 最大5件を <see cref="CommentSelector"/> で選び、直近履歴 (historyTurns ターン) で応答生成
/// - <see cref="LiveSessionOptions.FreetalkAfterSec"/> 秒無コメントで自動フリートーク
/// - フィルタ違反時は応答を破棄し、そのターンの user メッセージも履歴から破棄
/// - キャンセル時に発話ログ末尾50件を要約して <see cref="SharedMemory"/> に保存
/// - ストリーミング (文単位・2キューTTS) / 非ストリーミング (一括生成) の両経路
/// </summary>
public sealed class LiveSession : IAsyncDisposable
{
    private readonly ICommentSource _source;
    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly SharedMemory _memory;
    private readonly ISynthesizer _synthesizer;
    private readonly IAudioSink _sink;
    private readonly TtsPipeline _ttsPipeline;
    private readonly CommentSelector _selector = new();
    private readonly int _historyTurns;
    private readonly bool _useStreaming;

    private readonly object _optionsLock = new();
    private readonly LiveSessionOptions _options;

    // 感情スタイルマップは SpeakerId / EmotionStyleIds が変わったときだけ作り直す
    private EmotionStyleMap? _styleMap;
    private int _styleMapSpeakerId = int.MinValue;
    private IReadOnlyDictionary<string, int>? _styleMapStyles;

    /// <summary>現在のセッション状態。</summary>
    public SessionState State { get; private set; } = SessionState.Stopped;

    /// <summary>ループ・状態遷移の通知 (UI・コンソールへ)。購読者例外はループを殺さない。</summary>
    public event Action<LiveEvent>? EventRaised;

    /// <param name="options">実行中に変更可能なパラメータ (初期値)。</param>
    /// <param name="source">コメント取得元。RunAsync 内で Start され、DisposeAsync で破棄される。</param>
    /// <param name="synthesizer">文 → wav の合成 (テスト用にインターフェース受け)。</param>
    /// <param name="sink">wav → 再生 (テスト用にインターフェース受け)。</param>
    /// <param name="historyTurns">Claude に渡す直近会話ターン数 (実行中は変えない)。</param>
    /// <param name="useStreaming">ストリーミング経路を使うか (実行中は変えない)。</param>
    public LiveSession(LiveSessionOptions options, ICommentSource source,
                       Persona persona, ModerationFilter filter, SharedMemory memory,
                       ISynthesizer synthesizer, IAudioSink sink,
                       int historyTurns = 12, bool useStreaming = true)
    {
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _historyTurns = historyTurns;
        _useStreaming = useStreaming;
        _ttsPipeline = new TtsPipeline(_synthesizer, _sink);
    }

    /// <summary>
    /// VOICEVOX / AudioPlayer を直接受け取る便宜オーバーロード
    /// (CLI・Studio 本番用。中で <see cref="VoicevoxSynthesizer"/> / <see cref="AudioPlayerSink"/> に包む)。
    /// </summary>
    public LiveSession(LiveSessionOptions options, ICommentSource source,
                       Persona persona, ModerationFilter filter, SharedMemory memory,
                       VoicevoxClient voicevox, AudioPlayer player,
                       int historyTurns = 12, bool useStreaming = true)
        : this(options, source, persona, filter, memory,
               new VoicevoxSynthesizer(voicevox), new AudioPlayerSink(player), historyTurns, useStreaming)
    {
    }

    /// <summary>実行中パラメータを変更する (スレッドセーフ)。</summary>
    public void UpdateOptions(Action<LiveSessionOptions> mutate)
    {
        if (mutate is null)
        {
            throw new ArgumentNullException(nameof(mutate));
        }
        lock (_optionsLock)
        {
            mutate(_options);
        }
    }

    /// <summary>配信ループを実行する。ct のキャンセルでループを抜け、配信メモを保存して終了する。</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        SetState(SessionState.Starting);
        _source.Start(ct);
        SetState(SessionState.Running);

        var history = new List<ChatMessage>();   // Claude に渡す会話履歴
        var topicsLog = new List<string>();       // 配信メモ用の発話ログ
        var lastActivity = DateTime.UtcNow;
        Exception? fault = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                LiveSessionOptions snap = SnapshotOptions();
                await Task.Delay(snap.CommentBatchInterval, ct);

                if (snap.Paused)
                {
                    // 一時停止中は応答生成もフリートークもしない。コメントは溜めたまま (Drain しない)。
                    // 再開直後に無コメント扱いで即フリートークしないよう、活動時刻も進めておく。
                    lastActivity = DateTime.UtcNow;
                    continue;
                }

                // 溜まっているコメントを広めに拾い、優先度 (初見・質問優先) で最大5件を選ぶ
                var drained = _source.Drain(50);
                var comments = _selector.Select(drained, 5);
                string userMessage;
                if (comments.Count > 0)
                {
                    Raise(new CommentsPicked(DateTimeOffset.Now, comments));
                    userMessage = LiveMessages.BuildUserMessage(comments);
                    lastActivity = DateTime.UtcNow;
                }
                else if (snap.FreetalkEnabled &&
                         (DateTime.UtcNow - lastActivity).TotalSeconds > snap.FreetalkAfterSec)
                {
                    Raise(new FreeTalkTriggered(DateTimeOffset.Now));
                    userMessage = LiveMessages.FreetalkPrompt;
                    lastActivity = DateTime.UtcNow;
                }
                else
                {
                    continue;
                }

                history.Add(new ChatMessage("user", userMessage));
                LiveMessages.TrimHistory(history, _historyTurns);

                string? reply;
                if (_useStreaming)
                {
                    // ストリーミング: トークン → 文分割 → 文ごとにフィルタ → 感情タグ分離 → 2キューで合成/再生を並行化
                    var spoken = new StringBuilder();
                    EmotionStyleMap styleMap = GetStyleMap(snap);
                    var segments = LiveSpeech.ToSegmentsAsync(
                        _persona.GenerateStreamAsync(history, maxTokens: 200, ct),
                        _filter, styleMap, spoken, ct);
                    try
                    {
                        await _ttsPipeline.RunAsync(segments, ct);
                    }
                    catch (FilterViolationException ex)
                    {
                        // フィルタ違反時: 応答を破棄し、そのターンの履歴も破棄して次へ (既存仕様に準拠)
                        Raise(new ReplySkipped(DateTimeOffset.Now, ex.Message));
                        history.RemoveAt(history.Count - 1);
                        continue;
                    }
                    reply = spoken.ToString();
                    if (reply.Length == 0)
                    {
                        // 発話内容が無かった (タグのみ等): 履歴を汚さず次へ
                        history.RemoveAt(history.Count - 1);
                        continue;
                    }
                }
                else
                {
                    // 旧経路: 一括生成 → フィルタ → 合成 → 再生
                    reply = await _persona.GenerateAsync(history, maxTokens: 200, ct);
                    if (!_filter.IsSafe(reply))
                    {
                        Raise(new ReplySkipped(DateTimeOffset.Now, $"フィルタに掛かった応答: {reply}"));
                        history.RemoveAt(history.Count - 1);
                        continue;
                    }
                    byte[] wav = await _synthesizer.SynthesizeAsync(new SpeechSegment(reply, snap.SpeakerId), ct);
                    await _sink.PlayAsync(wav, ct);
                }

                history.Add(new ChatMessage("assistant", reply));
                topicsLog.Add(reply);
                Raise(new ReplySpoken(DateTimeOffset.Now, reply));
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C / Stop による正常終了
        }
        catch (Exception ex)
        {
            fault = ex;
        }
        finally
        {
            SetState(SessionState.Stopping);
            await SaveStreamNoteAsync(topicsLog);
            SetState(fault is null ? SessionState.Stopped : SessionState.Faulted);
        }

        if (fault is not null)
        {
            // 致命例外は CLI (従来どおりクラッシュ) / Studio (Faulted 検知) の双方が拾えるよう再スロー
            throw fault;
        }
    }

    // 配信メモを生成して SharedMemory に保存する (発話ログ末尾50件を要約)。topicsLog が空なら何もしない。
    private async Task SaveStreamNoteAsync(List<string> topicsLog)
    {
        if (topicsLog.Count == 0)
        {
            return;
        }
        var summaryRequest = new List<ChatMessage>
        {
            new("user", LiveMessages.BuildSummaryRequest(topicsLog)),
        };
        try
        {
            string summary = await _persona.GenerateAsync(summaryRequest, maxTokens: 150, CancellationToken.None);
            _memory.AddStreamNote(summary);
            Raise(new StreamNoteSaved(DateTimeOffset.Now, summary));
        }
        catch (Exception ex)
        {
            // 配信メモ生成失敗はループを落とさない (ログ相当は Skipped 理由として通知)
            Raise(new ReplySkipped(DateTimeOffset.Now, $"配信メモ生成失敗: {ex.Message}"));
        }
    }

    private LiveSessionOptions SnapshotOptions()
    {
        lock (_optionsLock)
        {
            return _options.Clone();
        }
    }

    // ループスレッド専用。SpeakerId / EmotionStyleIds が変わったときだけ EmotionStyleMap を作り直す。
    private EmotionStyleMap GetStyleMap(LiveSessionOptions snap)
    {
        if (_styleMap is null || _styleMapSpeakerId != snap.SpeakerId ||
            !ReferenceEquals(_styleMapStyles, snap.EmotionStyleIds))
        {
            _styleMap = new EmotionStyleMap(snap.SpeakerId, snap.EmotionStyleIds);
            _styleMapSpeakerId = snap.SpeakerId;
            _styleMapStyles = snap.EmotionStyleIds;
        }
        return _styleMap;
    }

    private void SetState(SessionState state)
    {
        State = state;
        Raise(new SessionStateChanged(DateTimeOffset.Now, state));
    }

    private void Raise(LiveEvent liveEvent)
    {
        Action<LiveEvent>? handler = EventRaised;
        if (handler is null)
        {
            return;
        }
        foreach (Delegate d in handler.GetInvocationList())
        {
            try
            {
                ((Action<LiveEvent>)d)(liveEvent);
            }
            catch
            {
                // 購読者 (UI・ログ) の例外で配信ループを殺さない
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        // コメントソースは RunAsync 内で Start する = このセッションが所有するので破棄する
        _source.Dispose();
        return ValueTask.CompletedTask;
    }
}
