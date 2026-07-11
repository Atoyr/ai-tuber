using System.Threading.Channels;

namespace Medoz.Voicevox;

/// <summary>合成対象の1文。<see cref="StyleId"/> は感情タグから解決した VOICEVOX スタイルID。</summary>
public readonly record struct SpeechSegment(string Text, int StyleId);

/// <summary>文 → wav バイト列 の合成抽象 (VOICEVOX 実装をフェイクに差し替え可能にする)。</summary>
public interface ISynthesizer
{
    Task<byte[]> SynthesizeAsync(SpeechSegment segment, CancellationToken ct = default);
}

/// <summary>wav バイト列 → 再生 の抽象 (AudioPlayer 実装をフェイクに差し替え可能にする)。</summary>
public interface IAudioSink
{
    Task PlayAsync(byte[] wav, CancellationToken ct = default);
}

/// <summary>
/// 2キュー式 TTS パイプライン (Phase H)。
/// 「合成キュー(文→wav)」と「再生キュー(wav→再生)」を <see cref="Channel"/> で分離し、
/// 合成と再生を並行化する。合成は逐次・再生も逐次だが、ある文を再生している間に
/// 次の文の合成を進められるため、文単位ストリーミングと組み合わせて発話遅延を減らせる。
/// 入力順 = 再生順 を保証する。Live / GameCommentary から共用する。
/// </summary>
public sealed class TtsPipeline
{
    private readonly ISynthesizer _synthesizer;
    private readonly IAudioSink _sink;
    private readonly int _capacity;

    /// <param name="capacity">合成済み wav を再生前にバッファできる最大数 (先読み合成の深さ)。</param>
    public TtsPipeline(ISynthesizer synthesizer, IAudioSink sink, int capacity = 4)
    {
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _capacity = capacity < 1 ? 1 : capacity;
    }

    /// <summary>
    /// セグメント列を合成しながら順次再生する。全再生の完了まで待機する。
    /// segments の列挙中に例外が出た場合 (フィルタ違反の中断など) は、
    /// 再生キューをその例外で打ち切り、例外を伝播する。
    /// ct のキャンセルで合成・再生の両方を停止する。
    /// </summary>
    public async Task RunAsync(IAsyncEnumerable<SpeechSegment> segments, CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        // 再生タスクが失敗したら合成側 (WriteAsync) がブロックし続けないよう、リンクしたトークンで停止させる。
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Task playTask = Task.Run(async () =>
        {
            try
            {
                await foreach (byte[] wav in channel.Reader.ReadAllAsync(linkedCts.Token))
                {
                    await _sink.PlayAsync(wav, linkedCts.Token);
                }
            }
            catch
            {
                linkedCts.Cancel(); // 再生失敗 → 合成側も止める
                throw;
            }
        }, CancellationToken.None);

        try
        {
            await foreach (SpeechSegment segment in segments.WithCancellation(linkedCts.Token))
            {
                byte[] wav = await _synthesizer.SynthesizeAsync(segment, linkedCts.Token);
                await channel.Writer.WriteAsync(wav, linkedCts.Token);
            }
            channel.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            // 合成 or セグメント供給側の例外で再生キューを打ち切る (既に積んだ分の再生も止める)。
            channel.Writer.TryComplete(ex);
            try
            {
                await playTask;
            }
            catch
            {
                // 再生側の例外は本来の例外を優先するため握りつぶす
            }
            throw;
        }

        await playTask;
    }
}

/// <summary>VOICEVOX 合成の <see cref="ISynthesizer"/> 実装。スタイルIDは各セグメントに従う。</summary>
public sealed class VoicevoxSynthesizer : ISynthesizer
{
    private readonly VoicevoxClient _client;

    public VoicevoxSynthesizer(VoicevoxClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<byte[]> SynthesizeAsync(SpeechSegment segment, CancellationToken ct = default)
        => _client.SynthesizeAsync(segment.Text, segment.StyleId, ct);
}

/// <summary><see cref="AudioPlayer"/> を <see cref="IAudioSink"/> として使うアダプタ。</summary>
public sealed class AudioPlayerSink : IAudioSink
{
    private readonly AudioPlayer _player;

    public AudioPlayerSink(AudioPlayer player)
        => _player = player ?? throw new ArgumentNullException(nameof(player));

    public Task PlayAsync(byte[] wav, CancellationToken ct = default)
        => _player.PlayWavAsync(wav, ct);
}
