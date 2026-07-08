namespace Medoz.MultiLLMClient;

/// <summary>会話履歴の1メッセージ。Role は "user" または "assistant"</summary>
public record ChatMessage(string Role, string Content);

/// <summary>Vision 用の画像データ。MediaType は "image/jpeg" など</summary>
public record ImageContent(string MediaType, string Base64Data);

/// <summary>
/// 会話履歴・Vision・streaming に対応したチャットクライアント。
/// 既存の <see cref="ILLMClient"/> を壊さずに拡張する。
/// </summary>
public interface IChatClient : ILLMClient
{
    Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                               int maxTokens = 300, CancellationToken ct = default);

    IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                               int maxTokens = 300, CancellationToken ct = default);

    Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                               int maxTokens = 150, CancellationToken ct = default);
}
