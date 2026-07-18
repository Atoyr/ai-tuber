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

    /// <param name="knowledgeName">
    /// knowledge/&lt;name&gt;.md の知識をシステムプロンプト末尾に結合する (ゲーム実況のゲーム知識など)。
    /// null なら従来どおり character.md + モード別md のみ。
    /// </param>
    public Persona(IChatClient client, PersonaPackage package, string modeFile, string? knowledgeName = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SystemPrompt = package.BuildSystemPrompt(modeFile, knowledgeName);
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
