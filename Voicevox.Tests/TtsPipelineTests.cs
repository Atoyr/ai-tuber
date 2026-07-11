using System.Collections.Concurrent;
using System.Text;

namespace Medoz.Voicevox.Tests;

public class TtsPipelineTests
{
    // ---- フェイク ----
    private sealed class DelegateSynth : ISynthesizer
    {
        private readonly Func<SpeechSegment, Task<byte[]>> _fn;
        public DelegateSynth(Func<SpeechSegment, Task<byte[]>> fn) => _fn = fn;
        public Task<byte[]> SynthesizeAsync(SpeechSegment segment, CancellationToken ct = default) => _fn(segment);
    }

    private sealed class DelegateSink : IAudioSink
    {
        private readonly Func<byte[], CancellationToken, Task> _fn;
        public DelegateSink(Func<byte[], CancellationToken, Task> fn) => _fn = fn;
        public Task PlayAsync(byte[] wav, CancellationToken ct = default) => _fn(wav, ct);
    }

    private static async IAsyncEnumerable<SpeechSegment> Segments(params string[] texts)
    {
        foreach (string t in texts)
        {
            await Task.Yield();
            yield return new SpeechSegment(t, 3);
        }
    }

    private static byte[] Wav(string s) => Encoding.UTF8.GetBytes(s);
    private static string Text(byte[] wav) => Encoding.UTF8.GetString(wav);

    [Fact]
    public async Task RunAsync_再生順は入力順を保つ()
    {
        var playLog = new List<string>();
        var synth = new DelegateSynth(seg => Task.FromResult(Wav(seg.Text)));
        var sink = new DelegateSink((wav, _) => { playLog.Add(Text(wav)); return Task.CompletedTask; });
        var pipeline = new TtsPipeline(synth, sink);

        await pipeline.RunAsync(Segments("a", "b", "c", "d"));

        Assert.Equal(new[] { "a", "b", "c", "d" }, playLog);
    }

    [Fact]
    public async Task RunAsync_再生中に次の文の合成が並行して進む()
    {
        var synthLog = new ConcurrentQueue<string>();
        var firstPlayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPlay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playLog = new ConcurrentQueue<string>();

        var synth = new DelegateSynth(seg =>
        {
            synthLog.Enqueue(seg.Text);
            return Task.FromResult(Wav(seg.Text));
        });
        var sink = new DelegateSink(async (wav, _) =>
        {
            string s = Text(wav);
            if (s == "one")
            {
                firstPlayStarted.SetResult();
                await releaseFirstPlay.Task; // 1件目の再生をテストが解放するまでブロック
            }
            playLog.Enqueue(s);
        });
        var pipeline = new TtsPipeline(synth, sink);

        Task run = pipeline.RunAsync(Segments("one", "two", "three"));

        await firstPlayStarted.Task; // "one" の再生が始まり、ブロック中

        // 再生がブロックされている間に、後続の合成が進む (並行) ことを確認する
        await WaitUntil(() => synthLog.Count == 3, TimeSpan.FromSeconds(5));
        Assert.Contains("two", synthLog);
        Assert.Contains("three", synthLog);
        Assert.Empty(playLog); // まだ1件も再生完了していない (one がブロック中)

        releaseFirstPlay.SetResult();
        await run;

        Assert.Equal(new[] { "one", "two", "three" }, playLog);
    }

    [Fact]
    public async Task RunAsync_キャンセルで停止する()
    {
        using var cts = new CancellationTokenSource();
        var synth = new DelegateSynth(seg => Task.FromResult(Wav(seg.Text)));
        var sink = new DelegateSink(async (_, ct) => await Task.Delay(Timeout.Infinite, ct)); // 永遠に再生し続ける
        var pipeline = new TtsPipeline(synth, sink);

        Task run = pipeline.RunAsync(Segments("x", "y", "z"), cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task RunAsync_供給側の例外を伝播し再生を打ち切る()
    {
        var playLog = new ConcurrentQueue<string>();
        var synth = new DelegateSynth(seg => Task.FromResult(Wav(seg.Text)));
        var sink = new DelegateSink((wav, _) => { playLog.Enqueue(Text(wav)); return Task.CompletedTask; });
        var pipeline = new TtsPipeline(synth, sink);

        async IAsyncEnumerable<SpeechSegment> Faulting()
        {
            await Task.Yield();
            yield return new SpeechSegment("ok", 3);
            throw new InvalidOperationException("boom");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.RunAsync(Faulting()));
    }

    private static async Task WaitUntil(Func<bool> cond, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("条件が時間内に満たされませんでした");
            }
            await Task.Delay(10);
        }
    }
}
