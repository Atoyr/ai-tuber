using Medoz.MultiLLMClient;

namespace Medoz.AiTuber.Core;

/// <summary>
/// キャラクター人格 (Python版 core/persona.py の PersonaClient 相当)。
/// ペルソナパッケージの character.md (共通人格) + モード別md を結合してシステムプロンプトとし、
/// IChatClient をラップする。配信も Twitter もここを通ることで同じ"魂"から発話が生成される。
/// </summary>
public class Persona
{
    private readonly IChatClient _client;

    public string SystemPrompt { get; }

    public Persona(IChatClient client, PersonaPackage package, string modeFile)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SystemPrompt = package.BuildSystemPrompt(modeFile);
    }

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
