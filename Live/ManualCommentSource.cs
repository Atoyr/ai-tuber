using System.Collections.Concurrent;

namespace Medoz.Live;

/// <summary>
/// UI から任意のコメントを差し込むためのコメントソース (docs/studio-architecture.md)。
/// <see cref="Enqueue"/> はスレッドセーフ。<see cref="Start"/> は何もしない
/// (外部から Enqueue されるのを待つだけ)。console モードや Studio のテストコメント注入に使う。
/// </summary>
public sealed class ManualCommentSource : ICommentSource
{
    private readonly ConcurrentQueue<Comment> _queue = new();

    /// <summary>コメントを1件積む (スレッドセーフ)。</summary>
    public void Enqueue(Comment comment)
        => _queue.Enqueue(comment ?? throw new ArgumentNullException(nameof(comment)));

    public void Start(CancellationToken ct)
    {
        // 外部 (UI) からの Enqueue を待つだけなので何もしない
    }

    public IReadOnlyList<Comment> Drain(int max)
    {
        if (max <= 0)
        {
            return Array.Empty<Comment>();
        }
        var picked = new List<Comment>();
        while (picked.Count < max && _queue.TryDequeue(out var comment))
        {
            picked.Add(comment);
        }
        return picked;
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
