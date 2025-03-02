using System;
using OpenAI.Chat;


namespace Medoz.MultiLLMClient;

public class OpenAIClientSettings
{
    public string ApiKey { get; init; }
    public string DefaultModel { get; init; } = "gpt-4o";

    public OpenAIClientSettings(string apiKey)
    {
        ApiKey = apiKey;
    }

    public OpenAIClientSettings(string apiKey, string defaultModel)
    {
        ApiKey = apiKey;
        DefaultModel = defaultModel;
    }
}

public class OpenAIClient : ILLMClient
{
    private readonly OpenAIClientSettings _settings;

    private ChatClient? _client;

    public OpenAIClient(OpenAIClientSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt)
    {
        if (_client == null)
        {
            _client = new ChatClient(model: _settings.DefaultModel, apiKey: _settings.ApiKey);
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
}