using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Medoz.MultiLLMClient;

public class GeminiClient : ILLMClient, IChatClient
{
    private readonly string _apiKey;

    private string _model { get;set;}
    private readonly HttpClient _httpClient;
    private string _apiEndpoint => $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
    private string _streamEndpoint => $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:streamGenerateContent?alt=sse&key={_apiKey}";

    public GeminiClient(string apiKey, string model = "gemini-2.5-flash")
        : this(apiKey, model, new HttpClient())
    {
    }

    // テストから HttpClient (モックハンドラ) を注入するためのコンストラクタ
    public GeminiClient(string apiKey, string model, HttpClient httpClient)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty");
        }
        _apiKey = apiKey;
        _model = model;
        _httpClient = httpClient;
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
    public async Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                            int maxTokens = 300, CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var contents = messages.Select(m => (object)new
        {
            role = ToGeminiRole(m.Role),
            parts = new object[] { new { text = m.Content } }
        }).ToArray();

        string json = BuildRequestJson(system, contents, maxTokens);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_apiEndpoint, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            return ExtractText(doc.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"API request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse API response: {ex.Message}", ex);
        }
    }

    public async Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                                     int maxTokens = 150, CancellationToken ct = default)
    {
        if (image is null)
            throw new ArgumentNullException(nameof(image));

        var contents = new object[]
        {
            new
            {
                role = "user",
                parts = new object[]
                {
                    new { inline_data = new { mime_type = image.MediaType, data = image.Base64Data } },
                    new { text }
                }
            }
        };

        string json = BuildRequestJson(system, contents, maxTokens);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_apiEndpoint, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using JsonDocument doc = JsonDocument.Parse(responseJson);
            return ExtractText(doc.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"API request failed: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse API response: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                              int maxTokens = 300,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var contents = messages.Select(m => (object)new
        {
            role = ToGeminiRole(m.Role),
            parts = new object[] { new { text = m.Content } }
        }).ToArray();

        string json = BuildRequestJson(system, contents, maxTokens);

        using var request = new HttpRequestMessage(HttpMethod.Post, _streamEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // SSE (alt=sse): "data: {json}" 行が流れてくる
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null || !line.StartsWith("data: "))
            {
                continue;
            }

            string data = line["data: ".Length..];
            string text;
            using (JsonDocument doc = JsonDocument.Parse(data))
            {
                text = ExtractText(doc.RootElement);
            }

            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    /// <summary>Claude 形式の role を Gemini 形式に変換する ("assistant" → "model")</summary>
    private static string ToGeminiRole(string role)
        => role == "assistant" ? "model" : role;

    private static string BuildRequestJson(string system, object[] contents, int maxTokens)
    {
        var requestObject = new
        {
            contents,
            systemInstruction = new
            {
                parts = new object[] { new { text = system } }
            },
            generationConfig = new
            {
                temperature = 0.7,
                topP = 0.95,
                topK = 40,
                maxOutputTokens = maxTokens
            }
        };
        return JsonSerializer.Serialize(requestObject);
    }

    /// <summary>candidates[0].content.parts[*].text を連結する。テキストが無ければ空文字</summary>
    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }
        if (!candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProperty))
            {
                builder.Append(textProperty.GetString());
            }
        }
        return builder.ToString();
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
