namespace Medoz.Live;

/// <summary>視聴者コメント1件</summary>
public record Comment(string Author, string Message);

/// <summary>
/// コメント取得元の抽象化。プラットフォーム依存部 (YouTube / コンソール / Twitch...) を差し替え可能にする。
/// </summary>
public interface ICommentSource : IDisposable
{
    /// <summary>バックグラウンドでコメント収集を開始する</summary>
    void Start(CancellationToken ct);

    /// <summary>溜まっているコメントを最大 max 件取り出す</summary>
    IReadOnlyList<Comment> Drain(int max);
}
