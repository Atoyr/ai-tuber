using Medoz.Studio.Events;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// <see cref="EventBroker"/> (SSE リングバッファ) のテスト。
/// 200件超で古いものが落ちること、接続直後のリプレイ順序、追記配信、購読解除を確認する。
/// </summary>
public class EventBrokerTests
{
    private static SseEvent Ev(int n) => new("log", $"{{\"n\":{n}}}");

    [Fact]
    public void 容量を超えたら古いイベントから落ちる()
    {
        var broker = new EventBroker(capacity: 200);
        for (int i = 0; i < 250; i++)
        {
            broker.Publish(Ev(i));
        }

        var snapshot = broker.Snapshot();

        Assert.Equal(200, snapshot.Count);
        Assert.Equal(Ev(50), snapshot[0]);    // 0〜49 は落ちている
        Assert.Equal(Ev(249), snapshot[^1]);
    }

    [Fact]
    public async Task 接続直後にバッファ内容が古い順でリプレイされる()
    {
        var broker = new EventBroker();
        broker.Publish(Ev(1));
        broker.Publish(Ev(2));
        broker.Publish(Ev(3));

        using var cts = new CancellationTokenSource();
        var received = new List<SseEvent>();
        await foreach (var sseEvent in broker.SubscribeAsync(cts.Token))
        {
            received.Add(sseEvent);
            if (received.Count == 3)
            {
                cts.Cancel(); // キャンセルで列挙が綺麗に終わる
            }
        }

        Assert.Equal(new[] { Ev(1), Ev(2), Ev(3) }, received);
    }

    [Fact]
    public async Task replay_false_なら過去ログを受け取らず新着だけ流れる()
    {
        // 配信画面 (overlay) は /api/events?replay=0 で購読する。
        // 接続時点までの発話が画面に一気に出ないことを保証する。
        var broker = new EventBroker();
        broker.Publish(Ev(1));
        broker.Publish(Ev(2));

        using var cts = new CancellationTokenSource();
        var received = new List<SseEvent>();
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var sseEvent in broker.SubscribeAsync(cts.Token, replay: false))
            {
                lock (received)
                {
                    received.Add(sseEvent);
                }
                cts.Cancel();
            }
        });

        // 購読登録が済むまで新着を送り続ける (リプレイが無いので受信で登録完了を待てない)
        for (int i = 0; i < 200; i++)
        {
            broker.Publish(Ev(3));
            lock (received)
            {
                if (received.Count > 0)
                {
                    break;
                }
            }
            await Task.Delay(10);
        }
        await subscribeTask;

        lock (received)
        {
            Assert.NotEmpty(received);
            Assert.All(received, e => Assert.Equal(Ev(3), e)); // Ev(1)/Ev(2) は届かない
        }
    }

    [Fact]
    public async Task リプレイ後の新着イベントも受け取れる()
    {
        var broker = new EventBroker();
        broker.Publish(Ev(1));

        using var cts = new CancellationTokenSource();
        var received = new List<SseEvent>();
        var subscribeTask = Task.Run(async () =>
        {
            await foreach (var sseEvent in broker.SubscribeAsync(cts.Token))
            {
                lock (received)
                {
                    received.Add(sseEvent);
                }
                if (sseEvent == Ev(2))
                {
                    cts.Cancel();
                }
            }
        });

        // 購読者がリプレイ (Ev(1)) を受け取って登録済みになるのを待ってから新着を発行
        await WaitForCountAsync(received, 1);
        broker.Publish(Ev(2));
        await subscribeTask;

        lock (received)
        {
            Assert.Equal(new[] { Ev(1), Ev(2) }, received);
        }
    }

    [Fact]
    public async Task 複数購読者が同じイベントを受け取り_切断した購読者は外れる()
    {
        var broker = new EventBroker();
        broker.Publish(Ev(0)); // リプレイで受信開始を確認できるようにする

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var received1 = new List<SseEvent>();
        var received2 = new List<SseEvent>();

        var task1 = CollectAsync(broker, received1, stopAt: Ev(1), cts1);
        var task2 = CollectAsync(broker, received2, stopAt: Ev(2), cts2);

        // 両購読者がリプレイ (Ev(0)) を受け取る = 登録済みになるのを待つ
        await WaitForCountAsync(received1, 1);
        await WaitForCountAsync(received2, 1);

        broker.Publish(Ev(1)); // 購読者1 はこれで抜ける
        await task1;

        broker.Publish(Ev(2)); // 購読者2 のみが受け取る
        await task2;

        lock (received1)
        {
            Assert.Equal(new[] { Ev(0), Ev(1) }, received1);
        }
        lock (received2)
        {
            Assert.Equal(new[] { Ev(0), Ev(1), Ev(2) }, received2);
        }
    }

    [Fact]
    public void ワイヤ形式はSSEの規約どおり()
    {
        var sseEvent = new SseEvent("reply", "{\"text\":\"こんにちは\"}");

        Assert.Equal("event: reply\ndata: {\"text\":\"こんにちは\"}\n\n", sseEvent.ToWireFormat());
    }

    private static Task CollectAsync(EventBroker broker, List<SseEvent> received, SseEvent stopAt,
                                     CancellationTokenSource cts)
        => Task.Run(async () =>
        {
            await foreach (var sseEvent in broker.SubscribeAsync(cts.Token))
            {
                lock (received)
                {
                    received.Add(sseEvent);
                }
                if (sseEvent == stopAt)
                {
                    cts.Cancel();
                }
            }
        });

    private static async Task WaitForCountAsync(List<SseEvent> list, int count)
    {
        for (int i = 0; i < 200; i++)
        {
            lock (list)
            {
                if (list.Count >= count)
                {
                    return;
                }
            }
            await Task.Delay(10);
        }
        throw new TimeoutException($"イベントが {count} 件届きませんでした。");
    }
}
