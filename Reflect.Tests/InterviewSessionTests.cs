using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;
using Medoz.Reflect;

namespace Medoz.Reflect.Tests;

public class InterviewSessionTests : IDisposable
{
    private readonly string _promptDir;
    private static readonly DateTime Now = new(2026, 7, 18, 12, 0, 0);
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    public InterviewSessionTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "interview-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "persona.json"),
            """{ "schemaVersion": 1, "name": "サンプル", "slug": "sample", "voice": { "speakerId": 3, "emotionStyles": {} } }""");
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはサンプルです。");
        File.WriteAllText(Path.Combine(_promptDir, "interview_system.md"), "壁打ちモードの指示。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    private InterviewSession CreateSession(FakeChatClient client)
    {
        var package = PersonaPackage.Load(_promptDir);
        var persona = new Persona(client, package, "interview_system.md");
        return new InterviewSession(persona, new ModerationFilter(BannedWords));
    }

    private static string Json(string add) =>
        $"{{\"observations\": [\"気づき\"], \"proposals\": [{{\"section\": \"性格の核\", \"add\": \"{add}\", \"reason\": \"会話より\"}}]}}";

    [Fact]
    public async Task StartAsync_SendsOpeningWithCharacterPrompt_AndReturnsFirstQuestion()
    {
        var client = new FakeChatClient("最初の質問です");
        var session = CreateSession(client);

        string question = await session.StartAsync("あなたはサンプルです。", new MemoryData(), Now);

        Assert.Equal("最初の質問です", question);
        Assert.NotNull(client.LastMessages);
        Assert.Single(client.LastMessages!);
        Assert.Contains("あなたはサンプルです。", client.LastMessages![0].Content);
    }

    [Fact]
    public async Task AskAsync_AccumulatesHistory()
    {
        var client = new FakeChatClient("質問1", "質問2");
        var session = CreateSession(client);

        await session.StartAsync("人格", new MemoryData(), Now);
        string reply = await session.AskAsync("元気なキャラにしたい");

        Assert.Equal("質問2", reply);
        // 履歴: opening(user) → 質問1(assistant) → 回答(user)
        Assert.Equal(3, client.LastMessages!.Count);
        Assert.Equal("assistant", client.LastMessages[1].Role);
        Assert.Equal("元気なキャラにしたい", client.LastMessages[2].Content);
    }

    [Fact]
    public async Task SummarizeAsync_AppendsSummarizeRequest_AndParsesResult()
    {
        var client = new FakeChatClient("質問1", Json("よく笑う"));
        var session = CreateSession(client);
        await session.StartAsync("人格", new MemoryData(), Now);

        ReflectionResult? result = await session.SummarizeAsync();

        Assert.NotNull(result);
        Assert.Equal("よく笑う", result!.Proposals[0].Add);
        Assert.Equal(InterviewMessages.SummarizeRequest, client.LastMessages![^1].Content);
    }

    [Fact]
    public async Task SummarizeAsync_Retries_WhenParseFails_WithoutPollutingHistory()
    {
        var client = new FakeChatClient("質問1", "JSONじゃない", Json("2回目で成功"));
        var session = CreateSession(client);
        await session.StartAsync("人格", new MemoryData(), Now);

        ReflectionResult? result = await session.SummarizeAsync();

        Assert.NotNull(result);
        Assert.Equal("2回目で成功", result!.Proposals[0].Add);
        // 再試行しても履歴は opening + 質問1 のまま(失敗した応答を積まない)
        Assert.Equal(3, client.LastMessages!.Count);
        Assert.Equal(3, client.CallCount);
    }

    [Fact]
    public async Task SummarizeAsync_Retries_WhenProposalContainsBannedWord()
    {
        var client = new FakeChatClient("質問1", Json("@メンションを多用する"), Json("安全な提案"));
        var session = CreateSession(client);
        await session.StartAsync("人格", new MemoryData(), Now);

        ReflectionResult? result = await session.SummarizeAsync();

        Assert.NotNull(result);
        Assert.Equal("安全な提案", result!.Proposals[0].Add);
    }

    [Fact]
    public async Task SummarizeAsync_ReturnsNull_AfterThreeFailures()
    {
        var client = new FakeChatClient("質問1", "ダメ1", "ダメ2", "ダメ3", Json("使われない"));
        var session = CreateSession(client);
        await session.StartAsync("人格", new MemoryData(), Now);

        ReflectionResult? result = await session.SummarizeAsync();

        Assert.Null(result);
        Assert.Equal(4, client.CallCount); // Start 1回 + まとめ3回
    }
}
