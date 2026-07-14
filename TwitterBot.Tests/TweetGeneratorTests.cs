using Medoz.AiTuber.Core;
using Medoz.TwitterBot;

namespace Medoz.TwitterBot.Tests;

public class TweetGeneratorTests : IDisposable
{
    private readonly string _promptDir;
    private readonly string _memoryPath;
    private static readonly DateTime Now = new(2026, 7, 8, 14, 30, 0);
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    public TweetGeneratorTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "twitterbot-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "persona.json"),
            """{ "schemaVersion": 1, "name": "ぽとふ", "slug": "potofu", "voice": { "speakerId": 3, "emotionStyles": {} } }""");
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはぽとふです。");
        File.WriteAllText(Path.Combine(_promptDir, "tweet_system.md"), "ツイートモードの指示。");
        _memoryPath = Path.Combine(_promptDir, "memory.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    private TweetGenerator CreateGenerator(FakeChatClient client, out SharedMemory memory)
    {
        var persona = new Persona(client, PersonaPackage.Load(_promptDir), "tweet_system.md");
        var filter = new ModerationFilter(BannedWords);
        memory = new SharedMemory(_memoryPath, recentTweetsKeep: 20);
        return new TweetGenerator(persona, filter, memory);
    }

    private static string Json(string tweet) => $"{{\"tweet\": \"{tweet}\", \"kind\": \"daily\"}}";

    [Fact]
    public async Task GenerateAsync_ReturnsTweet_OnFirstSuccess()
    {
        var client = new FakeChatClient(Json("今日はいい天気"));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal("今日はいい天気", result);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenParseFails()
    {
        var client = new FakeChatClient("これはJSONじゃない", Json("2回目で成功"));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal("2回目で成功", result);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenTooLong()
    {
        string tooLong = new string('あ', 141);
        var client = new FakeChatClient(Json(tooLong), Json("短いツイート"));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal("短いツイート", result);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Accepts_Exactly140Chars()
    {
        string exactly140 = new string('あ', 140);
        var client = new FakeChatClient(Json(exactly140));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal(exactly140, result);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenBannedWord()
    {
        // "@" は禁止ワード
        var client = new FakeChatClient(Json("@メンション付き"), Json("安全なツイート"));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal("安全なツイート", result);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenDuplicate()
    {
        var client = new FakeChatClient(Json("既出ツイート"), Json("新しいツイート"));
        var generator = CreateGenerator(client, out var memory);
        memory.AddTweet("既出ツイート"); // recent_tweets に登録済み

        string? result = await generator.GenerateAsync(Now);

        Assert.Equal("新しいツイート", result);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_AfterThreeFailures()
    {
        var client = new FakeChatClient("ダメ1", "ダメ2", "ダメ3", Json("使われないはず"));
        var generator = CreateGenerator(client, out _);

        string? result = await generator.GenerateAsync(Now);

        Assert.Null(result);
        Assert.Equal(3, client.CallCount); // 最大3回で打ち切り
    }
}
