namespace Medoz.Voicevox.Tests;

public class EmotionTagParserTests
{
    [Fact]
    public void Parse_先頭タグを本文から分離する()
    {
        var r = EmotionTagParser.Parse("[joy]えへへ、嬉しい!");
        Assert.Equal("joy", r.Emotion);
        Assert.Equal("えへへ、嬉しい!", r.Text);
    }

    [Fact]
    public void Parse_タグ無しは本文のみ_感情は空()
    {
        var r = EmotionTagParser.Parse("ふつうの発話だよ");
        Assert.Equal("", r.Emotion);
        Assert.Equal("ふつうの発話だよ", r.Text);
    }

    [Fact]
    public void Parse_タグは小文字化しタグ後の空白も除去する()
    {
        var r = EmotionTagParser.Parse("[JOY]  やった");
        Assert.Equal("joy", r.Emotion);
        Assert.Equal("やった", r.Text);
    }

    [Fact]
    public void Parse_未知タグもタグとして分離する()
    {
        var r = EmotionTagParser.Parse("[sleepy]ねむい");
        Assert.Equal("sleepy", r.Emotion);
        Assert.Equal("ねむい", r.Text);
    }

    [Fact]
    public void Parse_空入力は空タグ空本文()
    {
        var r = EmotionTagParser.Parse("");
        Assert.Equal("", r.Emotion);
        Assert.Equal("", r.Text);
    }

    [Fact]
    public void Parse_文中の角括弧はタグ扱いしない()
    {
        var r = EmotionTagParser.Parse("これは[joy]じゃない");
        Assert.Equal("", r.Emotion);
        Assert.Equal("これは[joy]じゃない", r.Text);
    }
}

public class EmotionStyleMapTests
{
    private static EmotionStyleMap Map() => new(
        defaultStyleId: 3,
        map: new Dictionary<string, int> { ["joy"] = 1, ["angry"] = 7 });

    [Fact]
    public void Resolve_既知タグはマップのスタイル()
    {
        Assert.Equal(1, Map().Resolve("joy"));
        Assert.Equal(7, Map().Resolve("angry"));
    }

    [Fact]
    public void Resolve_未知タグと空はデフォルト()
    {
        Assert.Equal(3, Map().Resolve("unknown"));
        Assert.Equal(3, Map().Resolve(""));
        Assert.Equal(3, Map().Resolve(null));
    }

    [Fact]
    public void Resolve_大文字小文字を無視する()
    {
        Assert.Equal(1, Map().Resolve("JOY"));
    }
}
