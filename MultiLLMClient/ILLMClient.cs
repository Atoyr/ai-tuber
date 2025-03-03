namespace Medoz.MultiLLMClient;

public interface ILLMClient
{
    void SetModel(string model);
    Task<string> GenerateTextAsync(string systemPrompt, string userPrompt);
}
