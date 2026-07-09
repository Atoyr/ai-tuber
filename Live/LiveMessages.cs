using Medoz.MultiLLMClient;

namespace Medoz.Live;

/// <summary>メインループで使うメッセージ組み立てヘルパー (Python版 live_aituber.py 相当)</summary>
public static class LiveMessages
{
    public const string FreetalkPrompt =
        "(コメントが途切れています。フリートークをしてください。" +
        "直近の話題の続きか、好きなものの話を自然に)";

    /// <summary>コメント一覧を「視聴者コメント:」形式のユーザーメッセージにする</summary>
    public static string BuildUserMessage(IReadOnlyList<Comment> comments)
    {
        var lines = comments.Select(c => $"{c.Author}: {c.Message}");
        return "視聴者コメント:\n" + string.Join("\n", lines);
    }

    /// <summary>会話履歴を直近 historyTurns ターン (user+assistant で historyTurns*2 件) に切り詰める</summary>
    public static void TrimHistory(List<ChatMessage> history, int historyTurns)
    {
        int keep = historyTurns * 2;
        if (history.Count > keep)
        {
            history.RemoveRange(0, history.Count - keep);
        }
    }

    /// <summary>配信終了時の要約依頼メッセージ。発話ログ末尾 50 件を渡す</summary>
    public static string BuildSummaryRequest(IReadOnlyList<string> topicsLog)
    {
        var recent = topicsLog.TakeLast(50);
        return "以下は今日の配信での自分の発話ログです。"
             + "配信の内容を1〜2文で要約してください(後でツイートの材料にします)。\n\n"
             + string.Join("\n", recent);
    }
}
