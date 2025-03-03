using System.Text;
using System.Text.Json;

namespace Medoz.MultiLLMClient;

public class ClaudeClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private string _model { get; set; }
    private readonly string _apiEndpoint = "https://api.anthropic.com/v1/messages";

    public ClaudeClient(string apiKey, string modelName = "claude-3-5-sonnet-20241022")
    {
        if(string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty");
        }
        _apiKey = apiKey;
        _model = modelName;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    public void SetModel(string model)
    {
        _model = model;
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
    {
        if (string.IsNullOrEmpty(userPrompt))
            throw new ArgumentNullException(nameof(userPrompt), "User prompt cannot be null or empty");

        // Create request payload
        var requestObject = new
        {
            model = _model,
            messages = new object[]
            {
                    new
                    {
                        role = "user",
                        content = userPrompt
                    }
            },
            system = systemPrompt,
            max_tokens = 1024,
            temperature = 0.7
        };

        var jsonContent = JsonSerializer.Serialize(requestObject);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        try
        {
            // Send request to Claude API
            var response = await _httpClient.PostAsync(_apiEndpoint, content);
            response.EnsureSuccessStatusCode();

            // Parse response
            var responseJson = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseJson);

            // Extract the generated text from response
            var contentElement = doc.RootElement.GetProperty("content");
            var generatedText = string.Empty;

            foreach (var part in contentElement.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var typeProperty) &&
                    typeProperty.GetString() == "text" &&
                    part.TryGetProperty("text", out var textProperty))
                {
                    generatedText += textProperty.GetString();
                }
            }

            return generatedText;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"API request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse API response: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"Unexpected error: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<string>> GetModelsAsync()
    {
        try
        {
            // Claude APIにリクエスト送信
            var response = await _httpClient.GetAsync(_apiEndpoint);
            response.EnsureSuccessStatusCode();

            // レスポンスの解析
            var responseJson = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseJson);

            var models = new List<string>();
            var modelsArray = doc.RootElement.GetProperty("models");

            foreach (var model in modelsArray.EnumerateArray())
            {
                if (model.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(nameProperty.GetString()))
                {
                    models.Add(nameProperty.GetString()!);
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to retrieve Claude models: {ex.Message}", ex);
        }
    }
}