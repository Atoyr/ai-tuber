using Medoz.AiTuber.Core;

namespace Medoz.BlogBot.Tests;

public class BlogGeneratorTests : IDisposable
{
    private readonly string _promptDir;
    private readonly string _memoryPath;
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 14, 30, 0, TimeSpan.FromHours(9));
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    /// <summary>検証を通る長さ (200字以上) の本文</summary>
    private static readonly string ValidBody = new('あ', 300);

    public BlogGeneratorTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "blogbot-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはぽとふです。");
        File.WriteAllText(Path.Combine(_promptDir, "blog_system.md"), "ブログモードの指示。");
        _memoryPath = Path.Combine(_promptDir, "memory.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    private BlogGenerator CreateGenerator(FakeChatClient client, out SharedMemory memory)
    {
        var persona = new Persona(client, _promptDir, "blog_system.md");
        var filter = new ModerationFilter(BannedWords);
        memory = new SharedMemory(_memoryPath);
        return new BlogGenerator(persona, filter, memory);
    }

    private static string Json(string title, string? body = null, string slug = "my-post", string kind = "daily")
        => $"{{\"title\": \"{title}\", \"slug\": \"{slug}\", \"description\": \"要約\", " +
           $"\"kind\": \"{kind}\", \"tags\": [\"タグ\"], \"body\": \"{body ?? ValidBody}\"}}";

    [Fact]
    public async Task GenerateAsync_ReturnsPost_OnFirstSuccess()
    {
        var client = new FakeChatClient(Json("はじめての記事"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.NotNull(post);
        Assert.Equal("はじめての記事", post.Title);
        Assert.Equal("my-post", post.Slug);
        Assert.Equal("daily", post.Kind);
        Assert.Equal(Now, post.Date);
        Assert.Equal("2026-07-08-my-post.md", post.FileName);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenParseFails()
    {
        var client = new FakeChatClient("これはJSONじゃない", Json("2回目で成功"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("2回目で成功", post?.Title);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenTitleTooLong()
    {
        var client = new FakeChatClient(Json(new string('あ', 41)), Json("短いタイトル"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("短いタイトル", post?.Title);
        Assert.Equal(2, client.CallCount);
    }

    [Theory]
    [InlineData(199)]  // 短すぎ
    [InlineData(2001)] // 長すぎ
    public async Task GenerateAsync_Retries_WhenBodyLengthOutOfRange(int length)
    {
        var client = new FakeChatClient(Json("範囲外", body: new string('あ', length)), Json("ちょうどいい"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("ちょうどいい", post?.Title);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenBannedWordInBody()
    {
        // "@" は禁止ワード
        var client = new FakeChatClient(Json("危険な記事", body: "@" + ValidBody), Json("安全な記事"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("安全な記事", post?.Title);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenTitleDuplicatesRecentPost()
    {
        var client = new FakeChatClient(Json("既出の記事"), Json("新しい記事"));
        var generator = CreateGenerator(client, out var memory);
        memory.AddPost("既出の記事");

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("新しい記事", post?.Title);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_FallsBackToDateSlug_WhenSlugInvalid()
    {
        var client = new FakeChatClient(Json("スラッグ不正", slug: "日本語スラッグ"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Equal("post-20260708", post?.Slug);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_AfterThreeFailures()
    {
        var client = new FakeChatClient("ダメ1", "ダメ2", "ダメ3", Json("使われないはず"));
        var generator = CreateGenerator(client, out _);

        BlogPost? post = await generator.GenerateAsync(Now);

        Assert.Null(post);
        Assert.Equal(3, client.CallCount);
    }
}
