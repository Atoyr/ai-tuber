using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Medoz.AiTuber.Core;
using Medoz.Live;
using Medoz.MultiLLMClient;
using Medoz.Voicevox;

namespace Medoz.Live.Tests;

public class LiveSessionTests : IDisposable
{
    private readonly string _personaDir;
    private readonly string _memoryPath;
    private readonly PersonaPackage _package;

    public LiveSessionTests()
    {
        _personaDir = Path.Combine(Path.GetTempPath(), "aituber-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_personaDir);
        File.WriteAllText(Path.Combine(_personaDir, "persona.json"),
            """{ "schemaVersion": 1, "name": "ぷる乃", "slug": "puruno", "voice": { "speakerId": 3, "emotionStyles": {} } }""");
        File.WriteAllText(Path.Combine(_personaDir, "character.md"), "あなたはぷる乃です。");
        File.WriteAllText(Path.Combine(_personaDir, "live_system.md"), "配信モードの指示。");
        _package = PersonaPackage.Load(_personaDir);
        _memoryPath = Path.Combine(_personaDir, "memory.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_personaDir))
        {
            Directory.Delete(_personaDir, recursive: true);
        }
    }

    private Persona MakePersona(FakeChatClient client) => new(client, _package, "live_system.md");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task コメント投入で応答が発話され再生される()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 999 };
        var manual = new ManualCommentSource();
        var chat = new FakeChatClient { StreamFactory = _ => new[] { "こんにちは。" } };
        var filter = new ModerationFilter(Array.Empty<string>());
        var memory = new SharedMemory(_memoryPath);
        var synth = new FakeSynthesizer();
        var sink = new FakeAudioSink();
        await using var session = new LiveSession(options, manual, MakePersona(chat), filter, memory, synth, sink);

        var spoken = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventRaised += e => { if (e is ReplySpoken r) spoken.TrySetResult(r.Text); };

        manual.Enqueue(new Comment("viewer", "やあ"));
        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        string text = await spoken.Task.WaitAsync(Timeout);
        cts.Cancel();
        await run.WaitAsync(Timeout);

        Assert.Equal("こんにちは。", text);
        Assert.NotEmpty(sink.Played);
        // 履歴に user + assistant が積まれている (次の合成呼び出しに前ターンが含まれるかは別テストで検証)
        Assert.Contains(chat.StreamCalls, call => call.Any(m => m.Role == "user"));
    }

    [Fact]
    public async Task フィルタ違反で応答が破棄されそのターンの履歴も破棄される()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 999 };
        var manual = new ManualCommentSource();
        // 直近 user メッセージに "bad" が含まれる回だけ禁止ワードを返す
        var chat = new FakeChatClient
        {
            StreamFactory = messages =>
                messages[^1].Content.Contains("bad") ? new[] { "殺す。" } : new[] { "やあ。" },
        };
        var filter = new ModerationFilter(new[] { "殺す" });
        var memory = new SharedMemory(_memoryPath);
        await using var session = new LiveSession(options, manual, MakePersona(chat), filter, memory,
            new FakeSynthesizer(), new FakeAudioSink());

        var skipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spoken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventRaised += e =>
        {
            if (e is ReplySkipped) skipped.TrySetResult();
            if (e is ReplySpoken) spoken.TrySetResult();
        };

        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        manual.Enqueue(new Comment("viewer", "bad"));
        await skipped.Task.WaitAsync(Timeout);

        manual.Enqueue(new Comment("viewer", "good"));
        await spoken.Task.WaitAsync(Timeout);
        cts.Cancel();
        await run.WaitAsync(Timeout);

        // 違反ターンの user メッセージが破棄されているため、成功ターンの合成には
        // "bad" を含むメッセージが残っていない (履歴 = [user(good)] のみ)。
        var goodCall = chat.StreamCalls.Last();
        Assert.DoesNotContain(goodCall, m => m.Content.Contains("bad"));
        Assert.Single(goodCall); // user(good) の1件のみ
    }

    [Fact]
    public async Task 無コメントでフリートークが発火する()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 0.05, FreetalkEnabled = true };
        var manual = new ManualCommentSource();
        var chat = new FakeChatClient { StreamFactory = _ => new[] { "ひとりごと。" } };
        var memory = new SharedMemory(_memoryPath);
        await using var session = new LiveSession(options, manual, MakePersona(chat),
            new ModerationFilter(Array.Empty<string>()), memory, new FakeSynthesizer(), new FakeAudioSink());

        var freetalk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventRaised += e => { if (e is FreeTalkTriggered) freetalk.TrySetResult(); };

        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        await freetalk.Task.WaitAsync(Timeout);
        cts.Cancel();
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public async Task フリートーク無効なら発火しない()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 0.02, FreetalkEnabled = false };
        var manual = new ManualCommentSource();
        var chat = new FakeChatClient { StreamFactory = _ => new[] { "ひとりごと。" } };
        var memory = new SharedMemory(_memoryPath);
        await using var session = new LiveSession(options, manual, MakePersona(chat),
            new ModerationFilter(Array.Empty<string>()), memory, new FakeSynthesizer(), new FakeAudioSink());

        bool freetalkFired = false;
        session.EventRaised += e => { if (e is FreeTalkTriggered) freetalkFired = true; };

        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        await Task.Delay(300); // フリートーク猶予 (0.02s) を十分に超える時間だけ回す
        cts.Cancel();
        await run.WaitAsync(Timeout);

        Assert.False(freetalkFired);
    }

    [Fact]
    public async Task キャンセルで配信メモが保存されStreamNoteSavedが発火する()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 999 };
        var manual = new ManualCommentSource();
        var chat = new FakeChatClient
        {
            StreamFactory = _ => new[] { "こんにちは。" },
            GenerateResult = "今日はあいさつをした配信でした。",
        };
        var memory = new SharedMemory(_memoryPath);
        await using var session = new LiveSession(options, manual, MakePersona(chat),
            new ModerationFilter(Array.Empty<string>()), memory, new FakeSynthesizer(), new FakeAudioSink());

        var spoken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var noteSaved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventRaised += e =>
        {
            if (e is ReplySpoken) spoken.TrySetResult();
            if (e is StreamNoteSaved n) noteSaved.TrySetResult(n.Summary);
        };

        manual.Enqueue(new Comment("viewer", "やあ"));
        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        await spoken.Task.WaitAsync(Timeout); // topicsLog に発話が積まれるまで待つ
        cts.Cancel();

        string summary = await noteSaved.Task.WaitAsync(Timeout);
        await run.WaitAsync(Timeout);

        Assert.Equal("今日はあいさつをした配信でした。", summary);
        var saved = memory.Load();
        Assert.Single(saved.StreamNotes);
        Assert.Equal("今日はあいさつをした配信でした。", saved.StreamNotes[0].Summary);
    }

    [Fact]
    public async Task 非ストリーミング経路でも応答が発話される()
    {
        var options = new LiveSessionOptions { CommentBatchSec = 0.02, FreetalkAfterSec = 999, SpeakerId = 7 };
        var manual = new ManualCommentSource();
        var chat = new FakeChatClient { GenerateResult = "一括生成の応答" };
        var synth = new FakeSynthesizer();
        var memory = new SharedMemory(_memoryPath);
        await using var session = new LiveSession(options, manual, MakePersona(chat),
            new ModerationFilter(Array.Empty<string>()), memory, synth, new FakeAudioSink(),
            useStreaming: false);

        var spoken = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.EventRaised += e => { if (e is ReplySpoken r) spoken.TrySetResult(r.Text); };

        manual.Enqueue(new Comment("viewer", "やあ"));
        using var cts = new CancellationTokenSource();
        Task run = session.RunAsync(cts.Token);

        string text = await spoken.Task.WaitAsync(Timeout);
        cts.Cancel();
        await run.WaitAsync(Timeout);

        Assert.Equal("一括生成の応答", text);
        // SpeakerId (7) がスタイルIDとして合成に渡っている
        Assert.Contains(synth.Segments, s => s.StyleId == 7);
    }

    [Fact]
    public void ManualCommentSource_Drainは最大件数まで取り出す()
    {
        var source = new ManualCommentSource();
        source.Enqueue(new Comment("a", "1"));
        source.Enqueue(new Comment("b", "2"));
        source.Enqueue(new Comment("c", "3"));

        var first = source.Drain(2);
        Assert.Equal(2, first.Count);
        Assert.Equal("a", first[0].Author);
        Assert.Equal("b", first[1].Author);

        var rest = source.Drain(10);
        Assert.Single(rest);
        Assert.Equal("c", rest[0].Author);

        Assert.Empty(source.Drain(5));
    }

    [Fact]
    public void CompositeCommentSource_各ソースから合計最大件数まで集める()
    {
        var a = new ManualCommentSource();
        var b = new ManualCommentSource();
        a.Enqueue(new Comment("a1", "x"));
        a.Enqueue(new Comment("a2", "x"));
        b.Enqueue(new Comment("b1", "y"));
        var composite = new CompositeCommentSource(a, b);

        var picked = composite.Drain(2);
        Assert.Equal(2, picked.Count);
        Assert.Equal("a1", picked[0].Author);
        Assert.Equal("a2", picked[1].Author); // 先頭ソースを使い切ってから次へ

        var rest = composite.Drain(10);
        Assert.Single(rest);
        Assert.Equal("b1", rest[0].Author);
    }

    [Fact]
    public void CompositeCommentSource_StartとDisposeを全ソースへ委譲する()
    {
        var a = new TrackingSource();
        var b = new TrackingSource();
        var composite = new CompositeCommentSource(a, b);

        composite.Start(CancellationToken.None);
        composite.Dispose();

        Assert.True(a.Started && b.Started);
        Assert.True(a.Disposed && b.Disposed);
    }

    // --- フェイク ---

    private sealed class FakeChatClient : IChatClient
    {
        public Func<IReadOnlyList<ChatMessage>, string[]>? StreamFactory { get; set; }
        public string GenerateResult { get; set; } = "";
        public List<ChatMessage[]> StreamCalls { get; } = new();
        public List<ChatMessage[]> GenerateCalls { get; } = new();

        public Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                          int maxTokens = 300, CancellationToken ct = default)
        {
            lock (GenerateCalls) { GenerateCalls.Add(messages.ToArray()); }
            return Task.FromResult(GenerateResult);
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                                  int maxTokens = 300,
                                                                  [EnumeratorCancellation] CancellationToken ct = default)
        {
            lock (StreamCalls) { StreamCalls.Add(messages.ToArray()); }
            string[] tokens = StreamFactory?.Invoke(messages) ?? Array.Empty<string>();
            foreach (string token in tokens)
            {
                ct.ThrowIfCancellationRequested();
                yield return token;
                await Task.Yield();
            }
        }

        public Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                                   int maxTokens = 150, CancellationToken ct = default)
            => Task.FromResult(GenerateResult);

        public void SetModel(string model) { }
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt) => Task.FromResult(GenerateResult);
        public Task<IEnumerable<string>> GetModelsAsync() => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }

    private sealed class FakeSynthesizer : ISynthesizer
    {
        public ConcurrentQueue<SpeechSegment> Segments { get; } = new();

        public Task<byte[]> SynthesizeAsync(SpeechSegment segment, CancellationToken ct = default)
        {
            Segments.Enqueue(segment);
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class FakeAudioSink : IAudioSink
    {
        public ConcurrentQueue<byte[]> Played { get; } = new();

        public Task PlayAsync(byte[] wav, CancellationToken ct = default)
        {
            Played.Enqueue(wav);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingSource : ICommentSource
    {
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public void Start(CancellationToken ct) => Started = true;
        public IReadOnlyList<Comment> Drain(int max) => Array.Empty<Comment>();
        public void Dispose() => Disposed = true;
    }
}
