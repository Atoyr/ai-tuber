namespace Medoz.GameCommentary;

/// <summary>
/// 実況生成用のユーザーメッセージ組み立てヘルパー (Python版 game_commentary.py の
/// get_claude_commentary の history_note 組み立て相当)。純粋関数なのでテストしやすい。
/// </summary>
public static class CommentaryMessages
{
    /// <summary>Vision に添えるユーザーテキストの本文。</summary>
    public const string CommentaryRequest = "今の画面を実況してください。";

    /// <summary>
    /// 直近の実況(最大 historyLimit 件)を文脈として付けたユーザーテキストを組み立てる。
    /// 履歴が空なら実況依頼のみ。Python版: "直近の実況: a / b\n今の画面を実況してください。"
    /// </summary>
    public static string BuildUserText(IReadOnlyList<string> history, int historyLimit)
    {
        if (history is null || history.Count == 0)
        {
            return CommentaryRequest;
        }

        // Python版 history[-HISTORY_LIMIT:] と同じく末尾 historyLimit 件だけを使う
        int skip = Math.Max(0, history.Count - historyLimit);
        string recent = string.Join(" / ", history.Skip(skip));
        return $"直近の実況: {recent}\n{CommentaryRequest}";
    }
}
