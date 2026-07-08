using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Medoz.MultiLLMClient;

public class ClaudeClient : ILLMClient, IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private string _model { get; set; }
    private readonly string _apiEndpoint = "https://api.anthropic.com/v1/messages";

    public ClaudeClient(string apiKey, string modelName = "claude-sonnet-4-6")
        : this(apiKey, modelName, new HttpClient())
    {
    }

    // テストから HttpClient (モックハンドラ) を注入するためのコンストラクタ
    public ClaudeClient(string apiKey, string modelName, HttpClient httpClient)
    {
        if(string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty");
        }
        _apiKey = apiKey;
        _model = modelName;
        _httpClient = httpClient;
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

    public async Task<string> GenerateAsync(string system, IReadOnlyList<ChatMessage> messages,
                                            int maxTokens = 300, CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var messageObjects = messages.Select(m => (object)new { role = m.Role, content = m.Content }).ToArray();
        string jsonContent = BuildRequestJson(system, messageObjects, maxTokens, stream: false);

        return await SendAndParseTextAsync(jsonContent, ct);
    }

    public async Task<string> GenerateWithImageAsync(string system, ImageContent image, string text,
                                                     int maxTokens = 150, CancellationToken ct = default)
    {
        if (image is null)
            throw new ArgumentNullException(nameof(image));

        var messageObjects = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = image.MediaType, data = image.Base64Data }
                    },
                    new { type = "text", text }
                }
            }
        };
        string jsonContent = BuildRequestJson(system, messageObjects, maxTokens, stream: false);

        return await SendAndParseTextAsync(jsonContent, ct);
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string system, IReadOnlyList<ChatMessage> messages,
                                                              int maxTokens = 300,
                                                              [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (messages is null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var messageObjects = messages.Select(m => (object)new { role = m.Role, content = m.Content }).ToArray();
        string jsonContent = BuildRequestJson(system, messageObjects, maxTokens, stream: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, _apiEndpoint)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // SSE: "event: xxx" 行と "data: {json}" 行が交互に流れてくる
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null || !line.StartsWith("data: "))
            {
                continue;
            }

            string data = line["data: ".Length..];
            string? text = null;
            bool stop = false;
            using (JsonDocument doc = JsonDocument.Parse(data))
            {
                var root = doc.RootElement;
                string? eventType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                if (eventType == "content_block_delta" &&
                    root.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("type", out var deltaType) &&
                    deltaType.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var textProp))
                {
                    text = textProp.GetString();
                }
                else if (eventType == "message_stop")
                {
                    stop = true;
                }
            }

            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
            if (stop)
            {
                yield break;
            }
        }
    }

    private string BuildRequestJson(string system, object[] messages, int maxTokens, bool stream)
    {
        var requestObject = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["system"] = system,
            ["max_tokens"] = maxTokens,
        };
        if (stream)
        {
            requestObject["stream"] = true;
        }
        return JsonSerializer.Serialize(requestObject);
    }

    private async Task<string> SendAndParseTextAsync(string jsonContent, CancellationToken ct)
    {
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

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

    private static string ExtractText(JsonElement root)
    {
        var builder = new StringBuilder();
        foreach (var part in root.GetProperty("content").EnumerateArray())
        {
            if (part.TryGetProperty("type", out var typeProperty) &&
                typeProperty.GetString() == "text" &&
                part.TryGetProperty("text", out var textProperty))
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