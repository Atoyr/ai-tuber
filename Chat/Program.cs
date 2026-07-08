﻿using Medoz.MultiLLMClient;
using Medoz.Voicevox;

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

Console.Write("システムプロンプトを入力してください: ");
string systemPrompt = Console.ReadLine() ?? "";

// VOICEVOX 発話オプション(デフォルト無効)
VoicevoxClient? voicevoxClient = null;
AudioPlayer? audioPlayer = null;
int speakerId = 3;

Console.Write("VOICEVOXで発話しますか? (y/N): ");
string? speakChoice = Console.ReadLine();
if (speakChoice?.Trim().ToLower() == "y")
{
    string voicevoxUrl = Environment.GetEnvironmentVariable("VOICEVOX_URL") ?? "http://127.0.0.1:50021";
    string defaultDeviceName = Environment.GetEnvironmentVariable("VOICEVOX_OUTPUT_DEVICE") ?? "CABLE Input";
    if (int.TryParse(Environment.GetEnvironmentVariable("VOICEVOX_SPEAKER_ID"), out int parsedSpeakerId))
    {
        speakerId = parsedSpeakerId;
    }

    try
    {
        var deviceNames = AudioPlayer.GetOutputDeviceNames();
        Console.WriteLine("出力デバイスを選択してください:");
        for (int i = 0; i < deviceNames.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {deviceNames[i]}");
        }
        Console.Write($"番号を入力してください (Enterで '{defaultDeviceName}' を含むデバイス): ");
        string? deviceChoice = Console.ReadLine();

        string outputDeviceName = defaultDeviceName;
        if (int.TryParse(deviceChoice, out int deviceNumber) && deviceNumber >= 1 && deviceNumber <= deviceNames.Count)
        {
            outputDeviceName = deviceNames[deviceNumber - 1];
        }

        voicevoxClient = new VoicevoxClient(voicevoxUrl);
        audioPlayer = new AudioPlayer(outputDeviceName);
        Console.WriteLine($"発話有効: {voicevoxUrl} speaker={speakerId} 出力デバイス={audioPlayer.DeviceName}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"発話の初期化に失敗しました: {ex.Message}");
        return;
    }
}

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

        if (voicevoxClient is not null && audioPlayer is not null)
        {
            byte[] wav = await voicevoxClient.SynthesizeAsync(response, speakerId);
            await audioPlayer.PlayWavAsync(wav);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"エラー: {ex.Message}");
    }
}

audioPlayer?.Dispose();
Console.WriteLine("チャットを終了します。");
