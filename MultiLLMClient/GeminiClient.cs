using System.Text;
using System.Text.Json;

namespace Medoz.MultiLLMClient;

public class GeminiClient : ILLMClient
{
    private readonly string _apiKey;

    private string _model { get;set;}
    private readonly HttpClient _httpClient;
    private string _apiEndpoint => $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

    public GeminiClient(string apiKey, string model = "gemini-1.5-pro")
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty");
        }
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient();
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
            contents = new object[]
            {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = userPrompt }
                        }
                    }
            },
            systemInstruction = new
            {
                parts = new object[]
                {
                        new { text = systemPrompt }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                topP = 0.95,
                topK = 40,
                maxOutputTokens = 2048
            }
        };

        var jsonContent = JsonSerializer.Serialize(requestObject);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        try
        {
            // Send request to Gemini API
            var response = await _httpClient.PostAsync(_apiEndpoint, content);
            response.EnsureSuccessStatusCode();

            // Parse response
            var responseJson = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(responseJson);

            // Extract generated text from response
            var candidatesElement = doc.RootElement.GetProperty("candidates")[0];
            var contentElement = candidatesElement.GetProperty("content");
            var partsElement = contentElement.GetProperty("parts")[0];
            var generatedText = partsElement.GetProperty("text").GetString();

            if (string.IsNullOrEmpty(generatedText))
            {
                throw new Exception("Failed to generate text");
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
            // Gemini APIにリクエスト送信
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
                    // 完全なモデル名からモデル名部分のみを抽出
                    string fullName = nameProperty.GetString()!;
                    string modelName = fullName.Substring(fullName.LastIndexOf('/') + 1);
                    models.Add(modelName);
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to retrieve Gemini models: {ex.Message}", ex);
        }
    }
}
