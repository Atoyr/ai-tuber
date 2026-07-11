using Medoz.Live;

namespace Medoz.Live.Tests;

public class CommentSelectorTests
{
    [Fact]
    public void Select_初見の著者を優先する()
    {
        var selector = new CommentSelector();
        // 花子を既出にしておく
        selector.Select(new List<Comment> { new("花子", "やあ") });

        var batch = new List<Comment>
        {
            new("花子", "また来たよ"),   // 既出 (スコア0)
            new("太郎", "はじめまして"), // 初見 (スコア2)
        };

        var picked = selector.Select(batch, max: 5);

        Assert.Equal("太郎", picked[0].Author); // 初見が先頭
        Assert.Equal("花子", picked[1].Author);
    }

    [Fact]
    public void Select_質問を優先する()
    {
        var selector = new CommentSelector();
        // 全員を既出にして初見スコアを消す
        selector.Select(new List<Comment> { new("A", "x"), new("B", "y") });

        var batch = new List<Comment>
        {
            new("A", "ふつうの発言"),
            new("B", "これってどうやるの?"), // 質問
        };

        var picked = selector.Select(batch, max: 5);

        Assert.Equal("B", picked[0].Author); // 質問が先頭
    }

    [Fact]
    public void Select_初見は質問より優先度が高い()
    {
        var selector = new CommentSelector();
        selector.Select(new List<Comment> { new("既出", "z") });

        var batch = new List<Comment>
        {
            new("既出", "これ質問なの?"), // 質問のみ (スコア1)
            new("新人", "こんにちは"),     // 初見のみ (スコア2)
        };

        var picked = selector.Select(batch, max: 5);

        Assert.Equal("新人", picked[0].Author);
        Assert.Equal("既出", picked[1].Author);
    }

    [Fact]
    public void Select_全角疑問符も質問扱い()
    {
        var selector = new CommentSelector();
        selector.Select(new List<Comment> { new("A", "x"), new("B", "y") });

        var batch = new List<Comment>
        {
            new("A", "ふつう"),
            new("B", "元気?"), // 全角
        };

        var picked = selector.Select(batch, max: 5);
        Assert.Equal("B", picked[0].Author);
    }

    [Fact]
    public void Select_最大件数で打ち切る()
    {
        var selector = new CommentSelector();
        var batch = Enumerable.Range(1, 8)
            .Select(i => new Comment($"user{i}", $"msg{i}"))
            .ToList();

        var picked = selector.Select(batch, max: 5);

        Assert.Equal(5, picked.Count);
    }

    [Fact]
    public void Select_同点は先着順()
    {
        var selector = new CommentSelector();
        // 全員初見・質問なし → 全員スコア2の同点 → 到着順で並ぶ
        var batch = new List<Comment>
        {
            new("1番目", "a"),
            new("2番目", "b"),
            new("3番目", "c"),
        };

        var picked = selector.Select(batch, max: 3);

        Assert.Equal(new[] { "1番目", "2番目", "3番目" }, picked.Select(c => c.Author).ToArray());
    }

    [Fact]
    public void Select_同一バッチ内で同じ著者は最初の1回だけ初見()
    {
        var selector = new CommentSelector();
        var batch = new List<Comment>
        {
            new("太郎", "1回目"), // 初見 (スコア2)
            new("太郎", "2回目"), // 既出扱い (スコア0)
        };

        var picked = selector.Select(batch, max: 5);

        Assert.Equal("1回目", picked[0].Message);
        Assert.Equal("2回目", picked[1].Message);
    }

    [Fact]
    public void Select_空バッチは空を返す()
    {
        var selector = new CommentSelector();
        Assert.Empty(selector.Select(new List<Comment>(), max: 5));
    }
}
