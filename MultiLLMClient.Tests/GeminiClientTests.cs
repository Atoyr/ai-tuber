using System.Net;
using System.Text;
using System.Text.Json;
using Medoz.MultiLLMClient;

namespace Medoz.MultiLLMClient.Tests;

/// <summary>
/// Gemini の IChatClient 実装を検証する。
/// HttpMessageHandler をスタブして実際のAPIは呼ばない。
/// </summary>
public class GeminiClientTests
{
    private const string ApiKey = "test-api-key";

    private static string CandidatesResponse(params string[] texts)
    {
        var parts = texts.Select(t => new { text = t });
        return JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts } } }
        });
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static (GeminiClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        var client = new GeminiClient(ApiKey, "gemini-2.5-flash", new HttpClient(handler));
        return (client, handler);
    }

    [Fact]
    public async Task GenerateAsync_MapsAssistantRoleToModel_AndSetsSystemInstruction()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(CandidatesResponse("ok")));

        var messages = new List<ChatMessage>
        {
            new("user", "こんにちは"),
            new("assistant", "やあ!"),
            new("user", "調子どう?"),
        };
        await client.GenerateAsync("system prompt", messages, maxTokens: 123);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("system prompt",
            root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(123, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());

        var contents = root.GetProperty("contents").EnumerateArray().ToArray();
        Assert.Equal(3, contents.Length);
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        // Gemini では assistant は "model"
        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("やあ!", contents[1].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user", contents[2].GetProperty("role").GetString());
    }

    [Fact]
    public async Task GenerateAsync_UsesGenerateContentEndpoint_WithApiKey()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(CandidatesResponse("ok")));

        await client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") });

        string url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("/models/gemini-2.5-flash:generateContent", url);
        Assert.Contains($"key={ApiKey}", url);
    }

    [Fact]
    public async Task GenerateAsync_ConcatenatesTextParts()
    {
        var (client, _) = CreateClient(_ => JsonResponse(CandidatesResponse("Hello, ", "world!")));

        var result = await client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") });

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsEmpty_WhenNoCandidates()
    {
        var (client, _) = CreateClient(_ => JsonResponse("{\"candidates\":[]}"));

        var result = await client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") });

        Assert.Equal("", result);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnEmptyMessages()
    {
        var (client, _) = CreateClient(_ => JsonResponse(CandidatesResponse("ok")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GenerateAsync("sys", new List<ChatMessage>()));
    }

    [Fact]
    public async Task GenerateWithImageAsync_BuildsInlineDataAndTextParts()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(CandidatesResponse("a cat")));

        var image = new ImageContent("image/jpeg", "BASE64DATA");
        await client.GenerateWithImageAsync("sys", image, "何が写ってる?", maxTokens: 150);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var content = doc.RootElement.GetProperty("contents").EnumerateArray().Single();
        Assert.Equal("user", content.GetProperty("role").GetString());

        var parts = content.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal(2, parts.Length);

        var inlineData = parts[0].GetProperty("inline_data");
        Assert.Equal("image/jpeg", inlineData.GetProperty("mime_type").GetString());
        Assert.Equal("BASE64DATA", inlineData.GetProperty("data").GetString());

        Assert.Equal("何が写ってる?", parts[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task GenerateStreamAsync_UsesSseEndpoint_AndYieldsTextChunks()
    {
        string sse =
            "data: " + CandidatesResponse("こんに") + "\n" +
            "\n" +
            "data: " + CandidatesResponse("ちは!") + "\n" +
            "\n";

        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        var chunks = new List<string>();
        await foreach (var chunk in client.GenerateStreamAsync(
            "sys", new List<ChatMessage> { new("user", "hi") }, maxTokens: 100))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "こんに", "ちは!" }, chunks);

        string url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains(":streamGenerateContent", url);
        Assert.Contains("alt=sse", url);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }
}
