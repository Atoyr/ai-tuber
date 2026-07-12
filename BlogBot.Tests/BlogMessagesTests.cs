using Medoz.AiTuber.Core;

namespace Medoz.BlogBot.Tests;

public class BlogMessagesTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 14, 30, 0);

    // --- BuildContext ---

    [Fact]
    public void BuildContext_ContainsDateAndWeekday()
    {
        string context = BlogMessages.BuildContext(new MemoryData(), Now);

        Assert.Contains("2026-07-08 14:30", context);
        Assert.Contains("水曜日", context);
    }

    [Fact]
    public void BuildContext_ContainsStreamNoteAndTweetsAndPosts()
    {
        var memory = new MemoryData
        {
            StreamNotes = { new StreamNote("2026-07-07 21:00", "レトロゲームをクリアした") },
            RecentTweets = { "今日はいい天気" },
            RecentPosts = { new PostNote("2026-07-06 20:00", "はじめてのシチュー") },
        };

        string context = BlogMessages.BuildContext(memory, Now);

        Assert.Contains("レトロゲームをクリアした", context);
        Assert.Contains("- 今日はいい天気", context);
        Assert.Contains("- 2026-07-06 20:00 はじめてのシチュー", context);
    }

    // --- ParseDraft ---

    [Fact]
    public void ParseDraft_ParsesFullJson()
    {
        string raw = """
            {"title": "タイトル", "slug": "my-post", "description": "要約",
             "kind": "stream", "tags": ["スープ", "配信"], "body": "本文だよ"}
            """;

        BlogDraft? draft = BlogMessages.ParseDraft(raw);

        Assert.NotNull(draft);
        Assert.Equal("タイトル", draft.Title);
        Assert.Equal("my-post", draft.Slug);
        Assert.Equal("要約", draft.Description);
        Assert.Equal("stream", draft.Kind);
        Assert.Equal(new[] { "スープ", "配信" }, draft.Tags);
        Assert.Equal("本文だよ", draft.Body);
    }

    [Fact]
    public void ParseDraft_StripsCodeFence()
    {
        string raw = "```json\n{\"title\": \"タイトル\", \"body\": \"本文\"}\n```";

        BlogDraft? draft = BlogMessages.ParseDraft(raw);

        Assert.NotNull(draft);
        Assert.Equal("タイトル", draft.Title);
    }

    [Theory]
    [InlineData("JSONじゃない")]
    [InlineData("[1, 2]")]
    [InlineData("{\"title\": \"タイトルのみ\"}")]
    [InlineData("{\"body\": \"本文のみ\"}")]
    [InlineData("{\"title\": \"\", \"body\": \"空タイトル\"}")]
    public void ParseDraft_ReturnsNull_OnInvalidInput(string raw)
    {
        Assert.Null(BlogMessages.ParseDraft(raw));
    }

    [Fact]
    public void ParseDraft_LimitsTagsToThree_AndSkipsEmpty()
    {
        string raw = """
            {"title": "t", "body": "b", "tags": ["a", "", "b", "c", "d"]}
            """;

        BlogDraft? draft = BlogMessages.ParseDraft(raw);

        Assert.NotNull(draft);
        Assert.Equal(new[] { "a", "b", "c" }, draft.Tags);
    }

    // --- slug / kind ---

    [Theory]
    [InlineData("my-post", true)]
    [InlineData("post-2026", true)]
    [InlineData("ab", false)]            // 短すぎ
    [InlineData("My-Post", false)]       // 大文字
    [InlineData("日本語", false)]
    [InlineData("a b", false)]
    [InlineData(null, false)]
    public void IsValidSlug_Validates(string? slug, bool expected)
    {
        Assert.Equal(expected, BlogMessages.IsValidSlug(slug));
    }

    [Fact]
    public void FallbackSlug_IsDateBased()
    {
        var now = new DateTimeOffset(2026, 7, 8, 14, 30, 0, TimeSpan.FromHours(9));
        Assert.Equal("post-20260708", BlogMessages.FallbackSlug(now));
    }

    [Theory]
    [InlineData("daily", "daily")]
    [InlineData("STREAM", "stream")]
    [InlineData("unknown", "daily")]
    [InlineData(null, "daily")]
    public void NormalizeKind_FallsBackToDaily(string? kind, string expected)
    {
        Assert.Equal(expected, BlogMessages.NormalizeKind(kind));
    }

    // --- BuildMarkdown ---

    [Fact]
    public void BuildMarkdown_BuildsFrontmatterContract()
    {
        var post = new BlogPost(
            Title: "はじめてのシチュー",
            Slug: "first-stew",
            Description: "シチューを作った話",
            Kind: "daily",
            Tags: new[] { "スープ" },
            Body: "今日はシチューを作ったよ。",
            Date: new DateTimeOffset(2026, 7, 8, 21, 0, 0, TimeSpan.FromHours(9)));

        string markdown = BlogMessages.BuildMarkdown(post);

        Assert.StartsWith("---\n", markdown);
        Assert.Contains("title: \"はじめてのシチュー\"\n", markdown);
        Assert.Contains("description: \"シチューを作った話\"\n", markdown);
        Assert.Contains("date: 2026-07-08T21:00:00+09:00\n", markdown);
        Assert.Contains("kind: daily\n", markdown);
        Assert.Contains("tags: [\"スープ\"]\n", markdown);
        Assert.EndsWith("---\n\n今日はシチューを作ったよ。\n", markdown);
        Assert.Equal("2026-07-08-first-stew.md", post.FileName);
    }

    [Fact]
    public void BuildMarkdown_EscapesQuotesAndNewlinesInTitle()
    {
        var post = new BlogPost(
            Title: "引用\"符\"と\n改行",
            Slug: "quote-test",
            Description: "d",
            Kind: "daily",
            Tags: Array.Empty<string>(),
            Body: "本文",
            Date: new DateTimeOffset(2026, 7, 8, 21, 0, 0, TimeSpan.FromHours(9)));

        string markdown = BlogMessages.BuildMarkdown(post);

        Assert.Contains("title: \"引用\\\"符\\\"と 改行\"\n", markdown);
        Assert.Contains("tags: []\n", markdown);
    }

    // --- CountLength ---

    [Fact]
    public void CountLength_CountsEmojiAsOne()
    {
        Assert.Equal(3, BlogMessages.CountLength("あ🍲a"));
    }
}
