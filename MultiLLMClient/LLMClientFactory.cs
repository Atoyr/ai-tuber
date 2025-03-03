namespace Medoz.MultiLLMClient;

public class LLMClientFactory
{
    public static ILLMClient CreateLLMClient<T>(string apiKey, string model)
    where T : ILLMClient, new()
    {
        return (T)Activator.CreateInstance(typeof(T), apiKey, model)!;
    }
}