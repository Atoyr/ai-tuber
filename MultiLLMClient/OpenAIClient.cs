using System;
using System.Text.Json;
using System.Net.Http.Headers;
using OpenAI.Chat;


namespace Medoz.MultiLLMClient;

public class OpenAIClient : ILLMClient
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
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
    {
        if (_client == null)
        {
            _client = new ChatClient(model: _model, apiKey: _apiKey);
        }

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userPrompt)
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