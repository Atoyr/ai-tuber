using System.Runtime.CompilerServices;
using Medoz.GameCommentary;
using Medoz.MultiLLMClient;

namespace Medoz.GameCommentary.Tests;

/// <summary>
/// テスト用フェイク IChatClient。GenerateWithImageAsync が呼ばれるたびに、
/// あらかじめ積んだ応答を順に返す。渡された画像・テキストは検証用に記録する。
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<string> _replies;
    private string _last = "";

    public int CallCount { get; private set; }
    public List<string> ReceivedTexts { get; } = new();
    public List<ImageContent> ReceivedImages { get; } = new();

    public FakeChatClient(params string[] replies)
    {
        _replies = new Queue<string>(replies);
    }

    public Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                               int maxTokens = 150, CancellationToken ct = default)
    {
        CallCount++;
        ReceivedImages.Add(image);
        ReceivedTexts.Add(text);
        if (_replies.Count > 0)
        {
            _last = _replies.Dequeue();
        }
        return Task.FromResult(_last);
    }

    public Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                      int maxTokens = 300, CancellationToken ct = default)
        => Task.FromResult(_last);

    public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                              int maxTokens = 300,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return await GenerateAsync(system, messages, maxTokens, ct);
    }

    // ILLMClient
    public void SetModel(string model) { }
    public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt) => Task.FromResult(_last);
    public Task<IEnumerable<string>> GetModelsAsync()
        => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
}

/// <summary>キャプチャのフェイク。固定バイト列を返すか、指定回数だけ例外を投げる。</summary>
internal sealed class FakeWindowCapture : IWindowCapture
{
    private readonly byte[] _bytes;
    private readonly Exception? _throw;

    public int CallCount { get; private set; }
    public string TargetTitle => "FakeWindow";

    public FakeWindowCapture(byte[]? bytes = null, Exception? toThrow = null)
    {
        _bytes = bytes ?? new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }; // ダミーJPEG
        _throw = toThrow;
    }

    public byte[] CaptureJpeg()
    {
        CallCount++;
        if (_throw is not null)
        {
            throw _throw;
        }
        return _bytes;
    }
}

/// <summary>発話のフェイク。呼ばれたテキストを記録する。</summary>
internal sealed class FakeSpeaker : ISpeaker
{
    public List<string> Spoken { get; } = new();

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        Spoken.Add(text);
        return Task.CompletedTask;
    }
}
