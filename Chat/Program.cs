﻿using Medoz.MultiLLMClient;

Console.WriteLine("使用するLLMを選択してください:");
Console.WriteLine("1. Gemini");
Console.WriteLine("2. OpenAI");
Console.WriteLine("3. Claude");
Console.Write("選択肢の番号を入力してください: ");
string? llmChoice = Console.ReadLine();

string apiKey = "";
string clientType = "";

switch (llmChoice)
{
    case "1":
        clientType = "gemini";
        break;
    case "2":
        clientType = "openai";
        break;
    case "3":
        clientType = "claude";
        break;
    default:
        Console.WriteLine("無効な選択肢です。");
        return;
}

Console.Write("APIキーを入力してください: ");
apiKey = Console.ReadLine() ?? "";
if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("APIキーが入力されていません。");
    return;
}

ILLMClient client = LLMClientFactory.CreateClient(clientType, apiKey);

string systemPrompt = "あなたは優秀なアシスタントです。";

while (true)
{
    Console.Write("質問を入力してください (終了するには exit と入力): ");
    string? input = Console.ReadLine();

    if (string.IsNullOrEmpty(input))
    {
        continue;
    }

    if (input.ToLower() == "exit")
    {
        break;
    }

    try
    {
        string response = await client.GenerateTextAsync(systemPrompt, input);
        Console.WriteLine("回答: " + response);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"エラー: {ex.Message}");
    }
}

Console.WriteLine("チャットを終了します。");
