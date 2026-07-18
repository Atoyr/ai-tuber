using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

public class WindowFinderTests
{
    private static readonly string[] Titles =
    {
        "Claude",
        "AITuber Studio - Google Chrome",
        "雀魂-じゃんたま-",
        "VOICEVOX - Ver. 0.14.10",
    };

    [Fact]
    public void IndexOfMatch_FindsByPartialTitle()
    {
        Assert.Equal(2, WindowFinder.IndexOfMatch(Titles, "雀魂"));
        Assert.Equal(3, WindowFinder.IndexOfMatch(Titles, "VOICEVOX"));
    }

    [Fact]
    public void IndexOfMatch_ReturnsFirstMatch_WhenSeveralContainFragment()
    {
        // "Claude" と "AITuber Studio - Google Chrome" 両方が "C" を含む → 先頭を採る
        Assert.Equal(0, WindowFinder.IndexOfMatch(Titles, "C"));
    }

    [Fact]
    public void IndexOfMatch_IsCaseSensitive()
    {
        // 従来 (WindowCapture) が StringComparison.Ordinal だったので挙動を変えない
        Assert.Equal(-1, WindowFinder.IndexOfMatch(Titles, "voicevox"));
    }

    [Fact]
    public void IndexOfMatch_ReturnsMinusOne_WhenNoMatch()
    {
        Assert.Equal(-1, WindowFinder.IndexOfMatch(Titles, "VALORANT"));
    }

    [Fact]
    public void BuildNotFoundMessage_ListsCandidates()
    {
        string message = WindowFinder.BuildNotFoundMessage("VALORANT", Titles);

        // 指定値と、書き換え先になる候補一覧がメッセージだけで分かること
        Assert.Contains("VALORANT", message);
        Assert.Contains("'雀魂-じゃんたま-'", message);
        Assert.Contains("'Claude'", message);
    }

    [Fact]
    public void Find_EmptyFragment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WindowFinder.Find(""));
    }
}
