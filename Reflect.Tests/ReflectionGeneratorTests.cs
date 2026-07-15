using Medoz.AiTuber.Core;
using Medoz.Reflect;

namespace Medoz.Reflect.Tests;

public class ReflectionGeneratorTests : IDisposable
{
    private readonly string _promptDir;
    private readonly string _memoryPath;
    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0);
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    public ReflectionGeneratorTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "reflect-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "persona.json"),
            """{ "schemaVersion": 1, "name": "サンプル", "slug": "sample", "voice": { "speakerId": 3, "emotionStyles": {} } }""");
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはサンプルです。");
        File.WriteAllText(Path.Combine(_promptDir, "reflect_system.md"), "振り返りモードの指示。");
        _memoryPath = Path.Combine(_promptDir, "memory.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    private ReflectionGenerator CreateGenerator(FakeChatClient client)
    {
        var package = PersonaPackage.Load(_promptDir);
        var persona = new Persona(client, package, "reflect_system.md");
        var filter = new ModerationFilter(BannedWords);
        var memory = new SharedMemory(_memoryPath);
        return new ReflectionGenerator(persona, filter, memory, package.CharacterPrompt);
    }

    private static string Json(string add) =>
        $"{{\"observations\": [\"気づき\"], \"proposals\": [{{\"section\": \"性格の核\", \"add\": \"{add}\", \"reason\": \"根拠\"}}]}}";

    [Fact]
    public async Task GenerateAsync_ReturnsResult_OnFirstSuccess()
    {
        var client = new FakeChatClient(Json("よく笑う"));
        var generator = CreateGenerator(client);

        ReflectionResult? result = await generator.GenerateAsync(Now);

        Assert.NotNull(result);
        Assert.Equal("よく笑う", result!.Proposals[0].Add);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_AllowsEmptyProposals()
    {
        var client = new FakeChatClient("{\"observations\": [], \"proposals\": []}");
        var generator = CreateGenerator(client);

        ReflectionResult? result = await generator.GenerateAsync(Now);

        Assert.NotNull(result);
        Assert.Empty(result!.Proposals);
        Assert.Equal(1, client.CallCount); // 空提案は再生成しない(正当な「今回は無し」)
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenParseFails()
    {
        var client = new FakeChatClient("これはJSONじゃない", Json("2回目で成功"));
        var generator = CreateGenerator(client);

        ReflectionResult? result = await generator.GenerateAsync(Now);

        Assert.NotNull(result);
        Assert.Equal("2回目で成功", result!.Proposals[0].Add);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_Retries_WhenProposalContainsBannedWord()
    {
        // "@" は禁止ワード。提案が character.md に入ると危ないので弾いて再生成
        var client = new FakeChatClient(Json("@メンションを多用する"), Json("安全な追記"));
        var generator = CreateGenerator(client);

        ReflectionResult? result = await generator.GenerateAsync(Now);

        Assert.NotNull(result);
        Assert.Equal("安全な追記", result!.Proposals[0].Add);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsNull_AfterThreeFailures()
    {
        var client = new FakeChatClient("ダメ1", "ダメ2", "ダメ3", Json("使われない"));
        var generator = CreateGenerator(client);

        ReflectionResult? result = await generator.GenerateAsync(Now);

        Assert.Null(result);
        Assert.Equal(3, client.CallCount);
    }
}
