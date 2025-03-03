namespace Medoz.MultiLLMClient;

public class LLMClientFactory
{
    public static ILLMClient CreateClient(string clientType, string apiKey, string? model = null)
    {
        switch (clientType.ToLower())
        {
            case "gemini":
                return model is null ? new GeminiClient(apiKey) : new GeminiClient(apiKey, model);
            case "openai":
                return model is null ? new OpenAIClient(apiKey) : new OpenAIClient(apiKey, model);
            case "claude":
                return model is null ? new ClaudeClient(apiKey) : new ClaudeClient(apiKey, model);
            default:
                throw new ArgumentException($"Invalid client type: {clientType}");
        }
    }
}