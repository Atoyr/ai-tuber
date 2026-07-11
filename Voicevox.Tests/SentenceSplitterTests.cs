namespace Medoz.Voicevox.Tests;

public class SentenceSplitterTests
{
    private static async IAsyncEnumerable<string> AsAsync(params string[] tokens)
    {
        foreach (string t in tokens)
        {
            await Task.Yield();
            yield return t;
        }
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> src)
    {
        var list = new List<string>();
        await foreach (string s in src)
        {
            list.Add(s);
        }
        return list;
    }

    [Fact]
    public void Split_句点で文を確定する()
    {
        var result = SentenceSplitter.Split("こんにちは。元気ですか?やってみる!");
        Assert.Equal(new[] { "こんにちは。", "元気ですか?", "やってみる!" }, result);
    }

    [Fact]
    public void Split_半角の感嘆符疑問符でも区切る()
    {
        var result = SentenceSplitter.Split("Hello! Are you ok? bye");
        Assert.Equal(new[] { "Hello!", "Are you ok?", "bye" }, result);
    }

    [Fact]
    public void Split_末尾の未確定分をフラッシュする()
    {
        var result = SentenceSplitter.Split("最初の文。区切りのない残り");
        Assert.Equal(new[] { "最初の文。", "区切りのない残り" }, result);
    }

    [Fact]
    public void Split_空入力は空リスト()
    {
        Assert.Empty(SentenceSplitter.Split(""));
        Assert.Empty(SentenceSplitter.Split("   "));
    }

    [Fact]
    public async Task SplitAsync_トークン境界をまたいで文を確定する()
    {
        // "こんにち" + "は。やって" + "みる!" と分割されて届いても文単位に組み直す
        var result = await Collect(SentenceSplitter.SplitAsync(AsAsync("こんにち", "は。やって", "みる!")));
        Assert.Equal(new[] { "こんにちは。", "やってみる!" }, result);
    }

    [Fact]
    public async Task SplitAsync_末尾未確定分をフラッシュ_空トークンは無視()
    {
        var result = await Collect(SentenceSplitter.SplitAsync(AsAsync("あ", "", "いう", "えお")));
        Assert.Equal(new[] { "あいうえお" }, result);
    }

    [Fact]
    public async Task SplitAsync_空ストリームは何も返さない()
    {
        var result = await Collect(SentenceSplitter.SplitAsync(AsAsync()));
        Assert.Empty(result);
    }
}
