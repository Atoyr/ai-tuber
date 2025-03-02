namespace Medoz.MultiLLMClient;

public interface ILLMClient
{
    Task<string> GenerateTextAsync(string systemPrompt, string userPrompt);
}
