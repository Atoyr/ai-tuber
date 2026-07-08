using System.Runtime.CompilerServices;
using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.AiTuber.Core.Tests;

public class PersonaTests : IDisposable
{
    private readonly string _promptDir;

    public PersonaTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "aituber-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはぷる乃です。");
        File.WriteAllText(Path.Combine(_promptDir, "live_system.md"), "配信モードの指示。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    [Fact]
    public void BuildSystemPrompt_JoinsCharacterAndModeWithSeparator()
    {
        string prompt = Persona.BuildSystemPrompt(_promptDir, "live_system.md");

        Assert.Equal("あなたはぷる乃です。\n\n---\n\n配信モードの指示。", prompt);
    }

    [Fact]
    public async Task GenerateAsync_PassesSystemPromptAndMessages_AndTrimsReply()
    {
        var stub = new StubChatClient { Reply = "  こんにちは!  \n" };
        var persona = new Persona(stub, _promptDir, "live_system.md");
        var messages = new List<ChatMessage> { new("user", "やあ") };

        string reply = await persona.GenerateAsync(messages, maxTokens: 200);

        Assert.Equal("こんにちは!", reply);
        Assert.Equal(persona.SystemPrompt, stub.LastSystem);
        Assert.Same(messages, stub.LastMessages);
        Assert.Equal(200, stub.LastMaxTokens);
    }

    [Fact]
    public async Task GenerateWithImageAsync_PassesSystemPromptAndImage_AndTrimsReply()
    {
        var stub = new StubChatClient { Reply = " 実況コメント \n" };
        var persona = new Persona(stub, _promptDir, "live_system.md");
        var image = new ImageContent("image/jpeg", "BASE64");

        string reply = await persona.GenerateWithImageAsync(image, "画面を実況して", maxTokens: 150);

        Assert.Equal("実況コメント", reply);
        Assert.Equal(persona.SystemPrompt, stub.LastSystem);
        Assert.Same(image, stub.LastImage);
        Assert.Equal("画面を実況して", stub.LastImageText);
        Assert.Equal(150, stub.LastMaxTokens);
    }

    private sealed class StubChatClient : IChatClient
    {
        public string Reply { get; set; } = "";
        public string? LastSystem { get; private set; }
        public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
        public ImageContent? LastImage { get; private set; }
        public string? LastImageText { get; private set; }
        public int LastMaxTokens { get; private set; }

        public Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                          int maxTokens = 300, CancellationToken ct = default)
        {
            LastSystem = system;
            LastMessages = messages;
            LastMaxTokens = maxTokens;
            return Task.FromResult(Reply);
        }

        public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                                  int maxTokens = 300,
                                                                  [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastSystem = system;
            LastMessages = messages;
            LastMaxTokens = maxTokens;
            yield return Reply;
            await Task.CompletedTask;
        }

        public Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                                   int maxTokens = 150, CancellationToken ct = default)
        {
            LastSystem = system;
            LastImage = image;
            LastImageText = text;
            LastMaxTokens = maxTokens;
            return Task.FromResult(Reply);
        }

        // ILLMClient
        public void SetModel(string model) { }
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
            => Task.FromResult(Reply);
        public Task<IEnumerable<string>> GetModelsAsync()
            => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    }
}
