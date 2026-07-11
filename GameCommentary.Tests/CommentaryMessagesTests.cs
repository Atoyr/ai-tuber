using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

public class CommentaryMessagesTests
{
    [Fact]
    public void BuildUserText_NoHistory_ReturnsRequestOnly()
    {
        string text = CommentaryMessages.BuildUserText(Array.Empty<string>(), 4);
        Assert.Equal(CommentaryMessages.CommentaryRequest, text);
    }

    [Fact]
    public void BuildUserText_WithHistory_PrefixesRecent()
    {
        string text = CommentaryMessages.BuildUserText(new[] { "A", "B" }, 4);
        Assert.Equal($"直近の実況: A / B\n{CommentaryMessages.CommentaryRequest}", text);
    }

    [Fact]
    public void BuildUserText_LimitsToLastFour()
    {
        var history = new[] { "1", "2", "3", "4", "5", "6" };
        string text = CommentaryMessages.BuildUserText(history, 4);

        // 末尾4件のみ(古い "1" "2" は含まれない)
        Assert.Contains("直近の実況: 3 / 4 / 5 / 6", text);
        Assert.DoesNotContain("1 / 2", text);
    }
}
