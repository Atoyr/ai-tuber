using Medoz.AiTuber.Core;

namespace Medoz.AiTuber.Core.Tests;

public class ModerationFilterTests
{
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    [Fact]
    public void IsSafe_ReturnsTrue_WhenNoBannedWord()
    {
        var filter = new ModerationFilter(BannedWords);
        Assert.True(filter.IsSafe("今日もみんなありがとう!たのしかった〜"));
    }

    [Theory]
    [InlineData("死ねばいいのに")]
    [InlineData("殺すぞ")]
    [InlineData("見て見て http://example.com")]
    [InlineData("@someone に返信")]
    public void IsSafe_ReturnsFalse_WhenBannedWordContained(string text)
    {
        var filter = new ModerationFilter(BannedWords);
        Assert.False(filter.IsSafe(text));
    }

    [Fact]
    public void FindBannedWord_ReturnsMatchedWord()
    {
        var filter = new ModerationFilter(BannedWords);
        Assert.Equal("http://", filter.FindBannedWord("これ見て http://example.com"));
        Assert.Null(filter.FindBannedWord("安全なテキスト"));
    }

    [Fact]
    public void IsSafe_ReturnsTrue_ForNullOrEmpty()
    {
        var filter = new ModerationFilter(BannedWords);
        Assert.True(filter.IsSafe(null));
        Assert.True(filter.IsSafe(""));
    }

    [Fact]
    public void IsSafe_ReturnsTrue_WhenBannedWordsEmpty()
    {
        var filter = new ModerationFilter(Array.Empty<string>());
        Assert.True(filter.IsSafe("死ね"));
    }
}
