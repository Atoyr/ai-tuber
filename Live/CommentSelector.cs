namespace Medoz.Live;

/// <summary>
/// バッチで拾ったコメントから Claude に渡す最大 N 件を優先度順に選ぶ純粋クラス (Phase H)。
/// 優先度は「初見(そのセッションで初めて発言した著者)」と「質問(? を含む)」を高くする。
/// - 初見: このセレクタ生成後、初めて登場した著者
/// - 質問: 半角 '?' または全角 '?' を含む
/// スコア = 初見(+2) + 質問(+1)。スコア降順、同点は先着順 (バッチ内の到着順)。
/// セッション状態 (既出著者) はインスタンスに保持する。
/// </summary>
public sealed class CommentSelector
{
    private readonly HashSet<string> _seenAuthors = new(StringComparer.Ordinal);

    private const int FirstSeenWeight = 2;
    private const int QuestionWeight = 1;

    /// <summary>
    /// batch から最大 max 件を優先度順に選んで返す。
    /// 選に漏れた著者も「発言した」ものとして既出扱いにする (次バッチ以降は初見でなくなる)。
    /// </summary>
    public IReadOnlyList<Comment> Select(IReadOnlyList<Comment> batch, int max = 5)
    {
        if (batch is null || batch.Count == 0 || max <= 0)
        {
            return Array.Empty<Comment>();
        }

        var scored = new List<(Comment Comment, int Score, int Index)>(batch.Count);
        for (int i = 0; i < batch.Count; i++)
        {
            Comment c = batch[i];
            // 同一バッチ内で著者が複数回出た場合、最初の1回だけを初見とする。
            bool firstSeen = _seenAuthors.Add(c.Author);
            int score = (firstSeen ? FirstSeenWeight : 0) + (IsQuestion(c.Message) ? QuestionWeight : 0);
            scored.Add((c, score, i));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)   // 同点は先着順 (OrderBy は安定だが明示する)
            .Take(max)
            .Select(x => x.Comment)
            .ToList();
    }

    private static bool IsQuestion(string? message)
        => !string.IsNullOrEmpty(message) && (message.Contains('?') || message.Contains('?'));
}
