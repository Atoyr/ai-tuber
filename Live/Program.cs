using Medoz.AiTuber.Core;
using Medoz.Live;
using Medoz.MultiLLMClient;
using Medoz.Voicevox;

// AITuber 配信本体 (Python版 live_aituber.py 相当)
// コメント取得 → LLM(人格)応答 → フィルタ → VOICEVOX → VB-CABLE → PuruPuruPNGTuber口パク
//
// 実行 (ローカルテスト):
//   dotnet run --project Live -- --console
//   dotnet run --project Live -- --console --provider gemini

bool consoleMode = args.Contains("--console");
string[] positional = args.Where(a => !a.StartsWith("--")).ToArray();

var config = AppConfig.LoadFromEnvironment();

// --provider <claude|gemini|openai> で環境変数 LLM_PROVIDER を上書きできる
int providerIndex = Array.IndexOf(args, "--provider");
if (providerIndex >= 0)
{
    if (providerIndex + 1 >= args.Length)
    {
        Console.WriteLine("--provider には claude / gemini / openai のいずれかを指定してください。");
        return 1;
    }
    config = config with { LlmProvider = args[providerIndex + 1] };
    positional = positional.Where(a => a != args[providerIndex + 1]).ToArray();
}

if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLower()))
{
    Console.WriteLine($"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai のいずれかを指定してください)");
    return 1;
}

string videoId = positional.Length > 0 ? positional[0] : config.YouTubeVideoId;

if (!consoleMode && string.IsNullOrEmpty(videoId))
{
    Console.WriteLine("YT_VIDEO_ID が未設定です。--console でローカルテストもできます。");
    return 1;
}
if (!consoleMode)
{
    Console.WriteLine("YouTube Live コメント取得は Phase E で実装予定です。現在は --console を使ってください。");
    return 1;
}

if (string.IsNullOrEmpty(config.LlmApiKey))
{
    Console.WriteLine($"環境変数 {config.LlmApiKeyEnvName} が未設定です (LLM_PROVIDER={config.LlmProvider})");
    return 1;
}

// キャラの表示名はコンソールログ用ラベル (人格そのものは prompts/character.md が唯一の真実)
string displayName = Environment.GetEnvironmentVariable("CHARACTER_NAME") is { Length: > 0 } name ? name : "ぷる乃";

IChatClient chatClient = LLMClientFactory.CreateChatClient(config.LlmProvider, config.LlmApiKey, config.LlmModel);
Console.WriteLine($"LLM: {config.LlmProvider} ({config.LlmModel})");
var persona = new Persona(chatClient, config.PromptDir, "live_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPath, config.RecentTweetsKeep);
var voicevox = new VoicevoxClient(config.VoicevoxUrl);

using var audioPlayer = new AudioPlayer(config.OutputDeviceName);
Console.WriteLine($"出力デバイス: {audioPlayer.DeviceName}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 即時終了せず、ループを抜けて配信メモを保存する
    cts.Cancel();
};

using ICommentSource commentSource = new ConsoleCommentSource();
commentSource.Start(cts.Token);

var history = new List<ChatMessage>();   // Claudeに渡す会話履歴
var topicsLog = new List<string>();      // 配信メモ用の発話ログ
var lastActivity = DateTime.UtcNow;

Console.WriteLine("=== 配信ループ開始 (Ctrl+Cで終了) ===");
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(config.CommentBatchSec), cts.Token);

        var comments = commentSource.Drain(5);
        string userMessage;
        if (comments.Count > 0)
        {
            userMessage = LiveMessages.BuildUserMessage(comments);
            lastActivity = DateTime.UtcNow;
        }
        else if ((DateTime.UtcNow - lastActivity).TotalSeconds > config.FreetalkAfterSec)
        {
            userMessage = LiveMessages.FreetalkPrompt;
            lastActivity = DateTime.UtcNow;
        }
        else
        {
            continue;
        }

        history.Add(new ChatMessage("user", userMessage));
        LiveMessages.TrimHistory(history, config.HistoryTurns);

        string reply = await persona.GenerateAsync(history, maxTokens: 200, cts.Token);

        if (!filter.IsSafe(reply))
        {
            // フィルタ違反時: 応答を破棄し、そのターンの履歴も破棄して次へ
            Console.WriteLine($"[skip] フィルタに掛かった応答: {reply}");
            history.RemoveAt(history.Count - 1);
            continue;
        }

        history.Add(new ChatMessage("assistant", reply));
        topicsLog.Add(reply);
        Console.WriteLine($"{displayName}: {reply}");

        byte[] wav = await voicevox.SynthesizeAsync(reply, config.SpeakerId, cts.Token);
        await audioPlayer.PlayWavAsync(wav, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C による正常終了
}
finally
{
    // 配信メモを生成してTwitterボットと共有 (発話ログ末尾50件を要約、memory.json に保存)
    if (topicsLog.Count > 0)
    {
        var summaryRequest = new List<ChatMessage>
        {
            new("user", LiveMessages.BuildSummaryRequest(topicsLog)),
        };
        try
        {
            string summary = await persona.GenerateAsync(summaryRequest, maxTokens: 150, CancellationToken.None);
            memory.AddStreamNote(summary);
            Console.WriteLine($"配信メモ保存: {summary}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"配信メモ生成失敗: {ex.Message}");
        }
    }
    Console.WriteLine("配信ループ終了");
}

return 0;
