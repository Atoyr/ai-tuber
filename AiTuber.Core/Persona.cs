using Medoz.MultiLLMClient;

namespace Medoz.AiTuber.Core;

/// <summary>
/// キャラクター人格 (Python版 core/persona.py の PersonaClient 相当)。
/// prompts/character.md (共通人格) + モード別md を結合してシステムプロンプトとし、
/// IChatClient をラップする。配信も Twitter もここを通ることで同じ"魂"から発話が生成される。
/// </summary>
public class Persona
{
    private readonly IChatClient _client;

    public string SystemPrompt { get; }

    public Persona(IChatClient client, string promptDir, string modeFile)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SystemPrompt = BuildSystemPrompt(promptDir, modeFile);
    }

    /// <summary>character.md (共通人格) + モード別指示 を結合してシステムプロンプトにする</summary>
    public static string BuildSystemPrompt(string promptDir, string modeFile)
    {
        string character = LoadPrompt(promptDir, "character.md");
        string mode = LoadPrompt(promptDir, modeFile);
        return $"{character}\n\n---\n\n{mode}";
    }

    public static string LoadPrompt(string promptDir, string name)
        => File.ReadAllText(Path.Combine(promptDir, name));

    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> messages,
                                            int maxTokens = 300, CancellationToken ct = default)
    {
        string reply = await _client.GenerateAsync(SystemPrompt, messages, maxTokens, ct);
        return reply.Trim();
    }

    public IAsyncEnumerable<string> GenerateStreamAsync(IReadOnlyList<ChatMessage> messages,
                                                        int maxTokens = 300, CancellationToken ct = default)
        => _client.GenerateStreamAsync(SystemPrompt, messages, maxTokens, ct);

    public async Task<string> GenerateWithImageAsync(ImageContent image, string text,
                                                     int maxTokens = 150, CancellationToken ct = default)
    {
        string reply = await _client.GenerateWithImageAsync(SystemPrompt, image, text, maxTokens, ct);
        return reply.Trim();
    }
}
