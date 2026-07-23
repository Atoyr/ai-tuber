using System.Threading.Channels;

namespace Medoz.Studio.Events;

/// <summary>
/// SSE イベントのハブ (docs/studio-architecture.md)。
/// - イベントはメモリ上のリングバッファ (最大 <see cref="Capacity"/> 件) に蓄積する
/// - 新規購読者には接続直後にリプレイしてから追記配信する (ページをリロードしてもログが消えない)
/// - 複数クライアント同時接続に対応。切断した購読者は綺麗に外れる
/// リングバッファのロジック自体をユニットテスト対象にする。
/// </summary>
public sealed class EventBroker
{
    /// <summary>リングバッファの最大保持件数。</summary>
    public const int DefaultCapacity = 200;

    private readonly object _lock = new();
    private readonly Queue<SseEvent> _buffer = new();
    private readonly List<Channel<SseEvent>> _subscribers = new();

    public int Capacity { get; }

    public EventBroker(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        Capacity = capacity;
    }

    /// <summary>リングバッファの現在の内容 (古い順)。テスト・リプレイ用。</summary>
    public IReadOnlyList<SseEvent> Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }

    /// <summary>イベントを発行する。バッファに積み、全購読者へ配る。</summary>
    public void Publish(SseEvent sseEvent)
    {
        ArgumentNullException.ThrowIfNull(sseEvent);
        Channel<SseEvent>[] targets;
        lock (_lock)
        {
            _buffer.Enqueue(sseEvent);
            while (_buffer.Count > Capacity)
            {
                _buffer.Dequeue(); // 200件を超えたら古いものから捨てる
            }
            targets = _subscribers.ToArray();
        }
        foreach (var channel in targets)
        {
            // Unbounded チャネルなので TryWrite は完了済みでない限り成功する
            channel.Writer.TryWrite(sseEvent);
        }
    }

    /// <summary>
    /// 購読を開始する。まずバッファ内容をリプレイし、その後の新着を ct キャンセルまで流し続ける。
    /// </summary>
    /// <param name="replay">
    /// false にするとリプレイを行わず、購読開始後の新着だけを流す。
    /// 配信画面 (overlay) のように「過去ログを画面に出したくない」購読者向け。
    /// </param>
    public async IAsyncEnumerable<SseEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        bool replay = true)
    {
        var channel = Channel.CreateUnbounded<SseEvent>();
        SseEvent[] replayed;
        lock (_lock)
        {
            replayed = replay ? _buffer.ToArray() : [];
            _subscribers.Add(channel);
        }

        try
        {
            foreach (var buffered in replayed)
            {
                yield return buffered;
            }
            while (true)
            {
                bool hasMore;
                try
                {
                    hasMore = await channel.Reader.WaitToReadAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // キャンセル (クライアント切断) は例外にせず列挙を綺麗に終える
                    hasMore = false;
                }
                if (!hasMore)
                {
                    break;
                }
                while (channel.Reader.TryRead(out var sseEvent))
                {
                    yield return sseEvent;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }
            channel.Writer.TryComplete();
        }
    }
}
