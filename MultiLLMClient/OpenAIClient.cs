using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Net.Http.Headers;
using OpenAI.Chat;
// Medoz.MultiLLMClient.ChatMessage (IChatClient.cs) との名前衝突を回避する
using OpenAIChatMessage = OpenAI.Chat.ChatMessage;


namespace Medoz.MultiLLMClient;

public class OpenAIClient : ILLMClient, IChatClient
{
    private readonly string _apiKey;
    private string _model { get; set; }
    private ChatClient? _client;

    private readonly HttpClient _httpClient;
    private readonly string _apiEndpoint = "https://api.openai.com/v1/models";

    public OpenAIClient(string apiKey, string model = "gpt-4o")
    {
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public void SetModel(string model)
    {
        _model = model;
        _client = null; // 次回呼び出し時に新しいモデルで作り直す
    }

    private ChatClient GetClient()
        => _client ??= new ChatClient(model: _model, apiKey: _apiKey);

    /// <summary>system + 会話履歴を OpenAI のメッセージ列に変換する</summary>
    private static OpenAIChatMessage[] BuildMessages(string system, IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<OpenAIChatMessage> { OpenAIChatMessage.CreateSystemMessage(system) };
        foreach (var m in messages)
        {
            result.Add(m.Role == "assistant"
                ? OpenAIChatMessage.CreateAssistantMessage(m.Content)
                : OpenAIChatMessage.CreateUserMessage(m.Content));
        }
        return result.ToArray();
    }

    public async Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                            int maxTokens = 300, CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var response = await GetClient().CompleteChatAsync(BuildMessages(system, messages), options, ct);
        return string.Concat(response.Value.Content.Select(part => part.Text));
    }

    public async Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                                     int maxTokens = 150, CancellationToken ct = default)
    {
        if (image is null)
            throw new ArgumentNullException(nameof(image));

        var imageBytes = BinaryData.FromBytes(Convert.FromBase64String(image.Base64Data));
        var userMessage = OpenAIChatMessage.CreateUserMessage(
            ChatMessageContentPart.CreateImagePart(imageBytes, image.MediaType),
            ChatMessageContentPart.CreateTextPart(text));

        var chatMessages = new OpenAIChatMessage[]
        {
            OpenAIChatMessage.CreateSystemMessage(system),
            userMessage,
        };

        var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var response = await GetClient().CompleteChatAsync(chatMessages, options, ct);
        return string.Concat(response.Value.Content.Select(part => part.Text));
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                              int maxTokens = 300,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };
        var updates = GetClient().CompleteChatStreamingAsync(BuildMessages(system, messages), options, ct);

        await foreach (var update in updates.WithCancellation(ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
    {
        if (_client == null)
        {
            _client = new ChatClient(model: _model, apiKey: _apiKey);
        }

        var messages = new OpenAIChatMessage[]
        {
            OpenAIChatMessage.CreateSystemMessage(systemPrompt),
            OpenAIChatMessage.CreateUserMessage(userPrompt)
        };

        var response = await _client.CompleteChatAsync(messages);
        var completion = response.Value;
        var message = completion.Content[0];
        return message.Text;
    }
    public async Task<IEnumerable<string>> GetModelsAsync()
    {
        try
        {
            // OpenAI APIにリクエスト送信
            var response = await _httpClient.GetAsync(_apiEndpoint);
            response.EnsureSuccessStatusCode();

            // レスポンスの解析
            var responseJson = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseJson);

            var models = new List<string>();
            var dataArray = doc.RootElement.GetProperty("data");

            foreach (var model in dataArray.EnumerateArray())
            {
                if (model.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(idProperty.GetString()))
                {
                    models.Add(idProperty.GetString()!);
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to retrieve OpenAI models: {ex.Message}", ex);
        }
    }
}