using System.Net;
using System.Text;
using System.Text.Json;
using Medoz.MultiLLMClient;

namespace Medoz.MultiLLMClient.Tests;

/// <summary>
/// リクエストJSONの組み立てとレスポンスパースを検証する。
/// HttpMessageHandler をスタブして実際のAPIは呼ばない。
/// </summary>
public class ClaudeClientTests
{
    private const string ApiKey = "test-api-key";

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string MessagesResponse(params string[] texts)
    {
        var content = texts.Select(t => new { type = "text", text = t });
        return JsonSerializer.Serialize(new { content });
    }

    private static (ClaudeClient Client, StubHttpMessageHandler Handler) CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        string model = "claude-sonnet-4-6")
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        var client = new ClaudeClient(ApiKey, model, new HttpClient(handler));
        return (client, handler);
    }

    [Fact]
    public async Task GenerateAsync_BuildsRequestJson_WithModelSystemMessagesAndMaxTokens()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(MessagesResponse("ok")));

        var messages = new List<ChatMessage>
        {
            new("user", "こんにちは"),
            new("assistant", "やあ!"),
            new("user", "調子どう?"),
        };
        await client.GenerateAsync("system prompt", messages, maxTokens: 123);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("claude-sonnet-4-6", root.GetProperty("model").GetString());
        Assert.Equal("system prompt", root.GetProperty("system").GetString());
        Assert.Equal(123, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("stream", out _));

        var jsonMessages = root.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(3, jsonMessages.Length);
        Assert.Equal("user", jsonMessages[0].GetProperty("role").GetString());
        Assert.Equal("こんにちは", jsonMessages[0].GetProperty("content").GetString());
        Assert.Equal("assistant", jsonMessages[1].GetProperty("role").GetString());
        Assert.Equal("調子どう?", jsonMessages[2].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GenerateAsync_SendsAuthHeaders()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(MessagesResponse("ok")));

        await client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") });

        var headers = handler.LastRequest!.Headers;
        Assert.Equal(ApiKey, headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task GenerateAsync_ParsesTextBlocks_ConcatenatingMultiple()
    {
        var (client, _) = CreateClient(_ => JsonResponse(MessagesResponse("Hello, ", "world!")));

        var result = await client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") });

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnEmptyMessages()
    {
        var (client, _) = CreateClient(_ => JsonResponse(MessagesResponse("ok")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GenerateAsync("sys", new List<ChatMessage>()));
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnHttpError()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":{}}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<Exception>(
            () => client.GenerateAsync("sys", new List<ChatMessage> { new("user", "hi") }));
    }

    [Fact]
    public async Task GenerateWithImageAsync_BuildsImageAndTextContentBlocks()
    {
        var (client, handler) = CreateClient(_ => JsonResponse(MessagesResponse("a cat")));

        var image = new ImageContent("image/jpeg", "BASE64DATA");
        await client.GenerateWithImageAsync("sys", image, "何が写ってる?", maxTokens: 150);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.Equal(150, root.GetProperty("max_tokens").GetInt32());

        var message = root.GetProperty("messages").EnumerateArray().Single();
        Assert.Equal("user", message.GetProperty("role").GetString());

        var blocks = message.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal(2, blocks.Length);

        Assert.Equal("image", blocks[0].GetProperty("type").GetString());
        var source = blocks[0].GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/jpeg", source.GetProperty("media_type").GetString());
        Assert.Equal("BASE64DATA", source.GetProperty("data").GetString());

        Assert.Equal("text", blocks[1].GetProperty("type").GetString());
        Assert.Equal("何が写ってる?", blocks[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task GenerateStreamAsync_SetsStreamFlag_AndYieldsTextDeltas()
    {
        const string sse =
            "event: message_start\n" +
            "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\"}}\n" +
            "\n" +
            "event: content_block_start\n" +
            "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n" +
            "\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"こんに\"}}\n" +
            "\n" +
            "event: content_block_delta\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"ちは!\"}}\n" +
            "\n" +
            "event: content_block_stop\n" +
            "data: {\"type\":\"content_block_stop\",\"index\":0}\n" +
            "\n" +
            "event: message_delta\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":5}}\n" +
            "\n" +
            "event: message_stop\n" +
            "data: {\"type\":\"message_stop\"}\n";

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

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(100, doc.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task GenerateStreamAsync_IgnoresNonTextDeltaEvents()
    {
        const string sse =
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{}\"}}\n" +
            "\n" +
            "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"only this\"}}\n" +
            "\n" +
            "data: {\"type\":\"message_stop\"}\n";

        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        var chunks = new List<string>();
        await foreach (var chunk in client.GenerateStreamAsync(
            "sys", new List<ChatMessage> { new("user", "hi") }))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(new[] { "only this" }, chunks);
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
