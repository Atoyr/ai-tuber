using System.Runtime.CompilerServices;
using Medoz.MultiLLMClient;

namespace Medoz.BlogBot.Tests;

/// <summary>
/// テスト用のフェイク IChatClient (TwitterBot.Tests と同じもの)。
/// GenerateAsync が呼ばれるたびに、あらかじめ積んだ応答を順に返す。
/// キューが空になったら最後の応答を返し続ける。
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _replies;
    private string _last = "";

    public int CallCount { get; private set; }

    public FakeChatClient(params string[] replies)
    {
        _replies = new Queue<string>(replies);
    }

    public Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                      int maxTokens = 300, CancellationToken ct = default)
    {
        CallCount++;
        if (_replies.Count > 0)
        {
            _last = _replies.Dequeue();
        }
        return Task.FromResult(_last);
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                              int maxTokens = 300,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await GenerateAsync(system, messages, maxTokens, ct);
    }

    public Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                               int maxTokens = 150, CancellationToken ct = default)
        => GenerateAsync(system, Array.Empty<ChatMessage>(), maxTokens, ct);

    // ILLMClient
    public void SetModel(string model) { }
    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
        => GenerateAsync(systemPrompt, Array.Empty<ChatMessage>());
    public Task<IEnumerable<string>> GetModelsAsync()
        => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
}
