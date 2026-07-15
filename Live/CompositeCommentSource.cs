namespace Medoz.Live;

/// <summary>
/// 複数のコメントソースを1つに束ねる (docs/studio-architecture.md)。
/// Studio では「選択したプラットフォームソース + <see cref="ManualCommentSource"/>」の合成で起動し、
/// 配信中にテストコメントを差し込めるようにする。Start / Drain / Dispose を各ソースへ委譲する。
/// </summary>
public sealed class CompositeCommentSource : ICommentSource
{
    private readonly IReadOnlyList<ICommentSource> _sources;

    public CompositeCommentSource(params ICommentSource[] sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }
        _sources = sources.ToArray();
    }

    public void Start(CancellationToken ct)
    {
        foreach (var source in _sources)
        {
            source.Start(ct);
        }
    }

    /// <summary>各ソースから順に集めて合計最大 max 件を返す。</summary>
    public IReadOnlyList<Comment> Drain(int max)
    {
        if (max <= 0)
        {
            return Array.Empty<Comment>();
        }
        var picked = new List<Comment>();
        foreach (var source in _sources)
        {
            if (picked.Count >= max)
            {
                break;
            }
            picked.AddRange(source.Drain(max - picked.Count));
        }
        return picked;
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            try
            {
                source.Dispose();
            }
            catch
            {
                // 個々の Dispose 失敗で他のソースの破棄を止めない
            }
        }
    }
}
