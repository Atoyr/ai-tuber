using Medoz.Live;
using Medoz.MultiLLMClient;

namespace Medoz.Live.Tests;

public class LiveMessagesTests
{
    [Fact]
    public void BuildUserMessage_FormatsCommentsWithHeader()
    {
        var comments = new List<Comment>
        {
            new("太郎", "こんにちは!"),
            new("花子", "初見です"),
        };

        string message = LiveMessages.BuildUserMessage(comments);

        Assert.Equal("視聴者コメント:\n太郎: こんにちは!\n花子: 初見です", message);
    }

    [Fact]
    public void TrimHistory_KeepsLastHistoryTurnsTimesTwo()
    {
        // 12ターン設定なら user+assistant で24件が上限
        var history = new List<ChatMessage>();
        for (int i = 1; i <= 30; i++)
        {
            history.Add(new ChatMessage(i % 2 == 1 ? "user" : "assistant", $"msg {i}"));
        }

        LiveMessages.TrimHistory(history, historyTurns: 12);

        Assert.Equal(24, history.Count);
        Assert.Equal("msg 7", history[0].Content);   // 古い6件が捨てられる
        Assert.Equal("msg 30", history[^1].Content);
    }

    [Fact]
    public void TrimHistory_DoesNothing_WhenUnderLimit()
    {
        var history = new List<ChatMessage> { new("user", "hi") };

        LiveMessages.TrimHistory(history, historyTurns: 12);

        Assert.Single(history);
    }

    [Fact]
    public void BuildSummaryRequest_UsesLast50Entries()
    {
        var topics = Enumerable.Range(1, 60).Select(i => $"topic {i}").ToList();

        string request = LiveMessages.BuildSummaryRequest(topics);

        Assert.StartsWith("以下は今日の配信での自分の発話ログです。", request);
        Assert.DoesNotContain("topic 10\n", request);  // 先頭10件は含まれない
        Assert.Contains("topic 11", request);          // 末尾50件の先頭
        Assert.Contains("topic 60", request);
    }
}
