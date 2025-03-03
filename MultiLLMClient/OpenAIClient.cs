using System;
using OpenAI.Chat;


namespace Medoz.MultiLLMClient;

public class OpenAIClient : ILLMClient
{
    private readonly string _apiKey;
    private string _model { get; set; }
    private ChatClient? _client;

    public OpenAIClient(string apiKey, string model = "gpt-4o")
    {
        _apiKey = apiKey;
        _model = model;
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
}