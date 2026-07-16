using Medoz.Studio.Apps;
using Medoz.Studio.Commentary;
using Medoz.Studio.Events;
using Medoz.Studio.LiveHosting;

namespace Medoz.Studio;

/// <summary>
/// live / voicevox / purupuru / commentary の状態を合成した SSE state イベントの発行係。
/// 前回発行時から変化があったときだけ流す (ポーリング由来の重複を抑える)。
/// </summary>
public sealed class StudioStateNotifier
{
    private readonly EventBroker _broker;
    private readonly AppLauncher _launcher;
    private readonly LiveSessionHost _liveHost;
    private readonly CommentarySessionHost _commentaryHost;
    private readonly object _lock = new();
    private (string Live, string Voicevox, string Purupuru, string Commentary)? _last;

    public StudioStateNotifier(EventBroker broker, AppLauncher launcher, LiveSessionHost liveHost,
                               CommentarySessionHost commentaryHost)
    {
        _broker = broker;
        _launcher = launcher;
        _liveHost = liveHost;
        _commentaryHost = commentaryHost;
    }

    /// <summary>現在の合成状態を (変化していれば) state イベントとして発行する。</summary>
    public void PublishIfChanged()
    {
        var current = (
            Live: _liveHost.Status().State,
            Voicevox: _launcher.Voicevox.Current().State.ToString(),
            Purupuru: _launcher.Purupuru.Current().State.ToString(),
            Commentary: _commentaryHost.Status().State);
        lock (_lock)
        {
            if (_last == current)
            {
                return;
            }
            _last = current;
        }
        _broker.Publish(SseEventMapper.State(current.Live, current.Voicevox, current.Purupuru, current.Commentary));
    }
}
