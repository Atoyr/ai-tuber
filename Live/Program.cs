using System.Text;
using Medoz.AiTuber.Core;
using Medoz.Live;
using Medoz.MultiLLMClient;
using Medoz.Voicevox;

// AITuber 配信本体 (Python版 live_aituber.py 相当)
// コメント取得 → LLM(人格)応答 → フィルタ → VOICEVOX → 仮想オーディオデバイス → PuruPuruPNGTuber口パク
//
// 実行 (ローカルテスト):
//   dotnet run --project Live -- --console
//   dotnet run --project Live -- --console --provider gemini
//   dotnet run --project Live -- --youtube <videoId|URL>           # YouTube Live のコメントを取得
//   dotnet run --project Live -- --list-devices                    # 出力デバイス一覧
//   dotnet run --project Live -- --console --device "VoiceMeeter"  # デバイス指定
//   dotnet run --project Live -- --console --select-device         # 起動時に対話で選ぶ
//   dotnet run --project Live -- --console --no-stream             # 旧経路 (一括生成) で発話する

// --list-devices は他の設定検証より前に処理する (環境変数なしでも動くようにする)
if (args.Contains("--list-devices"))
{
    var deviceNames = AudioPlayer.GetOutputDeviceNames();
    Console.WriteLine("利用可能な出力デバイス:");
    for (int i = 0; i < deviceNames.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {deviceNames[i]}");
    }
    return 0;
}

bool consoleMode = args.Contains("--console");
bool selectDevice = args.Contains("--select-device");
bool noStream = args.Contains("--no-stream"); // 旧経路 (一括生成) に切り替える
string[] positional = args.Where(a => !a.StartsWith("--")).ToArray();

var config = AppConfig.LoadFromEnvironment();
if (noStream)
{
    config = config with { UseStreaming = false };
}

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

// --device <name> で VOICEVOX_OUTPUT_DEVICE を上書きできる (部分一致)
int deviceArgIndex = Array.IndexOf(args, "--device");
if (deviceArgIndex >= 0)
{
    if (deviceArgIndex + 1 >= args.Length)
    {
        Console.WriteLine("--device には出力デバイス名 (部分一致) を指定してください。");
        return 1;
    }
    config = config with { OutputDeviceName = args[deviceArgIndex + 1] };
    positional = positional.Where(a => a != args[deviceArgIndex + 1]).ToArray();
}

// --youtube <videoId|URL> で YouTube Live のコメントを取得する
string? youtubeVideoArg = null;
int youtubeArgIndex = Array.IndexOf(args, "--youtube");
if (youtubeArgIndex >= 0)
{
    if (youtubeArgIndex + 1 >= args.Length)
    {
        Console.WriteLine("--youtube には videoId または YouTube の URL を指定してください。");
        return 1;
    }
    youtubeVideoArg = args[youtubeArgIndex + 1];
    positional = positional.Where(a => a != youtubeVideoArg).ToArray();
}
bool youtubeMode = youtubeVideoArg is not null;

if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLower()))
{
    Console.WriteLine($"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai のいずれかを指定してください)");
    return 1;
}

// videoId は --youtube の値 → 位置引数 → 環境変数 YT_VIDEO_ID の順で解決する
string videoId = youtubeVideoArg
    ?? (positional.Length > 0 ? positional[0] : config.YouTubeVideoId);

if (!consoleMode && !youtubeMode && string.IsNullOrEmpty(videoId))
{
    Console.WriteLine("YT_VIDEO_ID が未設定です。--console でローカルテスト、または --youtube <videoId|URL> を指定してください。");
    return 1;
}
// videoId が環境変数から取れていれば --youtube 省略でも YouTube モードにする
if (!consoleMode && !string.IsNullOrEmpty(videoId))
{
    youtubeMode = true;
}
if (!consoleMode && !youtubeMode)
{
    Console.WriteLine("--console でローカルテスト、または --youtube <videoId|URL> を指定してください。");
    return 1;
}
if (youtubeMode && string.IsNullOrEmpty(config.YouTubeApiKey))
{
    Console.WriteLine("環境変数 YOUTUBE_API_KEY が未設定です (YouTube Data API v3 のキーが必要です)。");
    return 1;
}

if (string.IsNullOrEmpty(config.LlmApiKey))
{
    Console.WriteLine($"環境変数 {config.LlmApiKeyEnvName} が未設定です (LLM_PROVIDER={config.LlmProvider})");
    return 1;
}

// キャラの表示名はコンソールログ用ラベル (人格そのものは prompts/character.md が唯一の真実)
string displayName = Environment.GetEnvironmentVariable("CHARACTER_NAME") is { Length: > 0 } name ? name : "ぽとふ";

IChatClient chatClient = LLMClientFactory.CreateChatClient(config.LlmProvider, config.LlmApiKey, config.LlmModel);
Console.WriteLine($"LLM: {config.LlmProvider} ({config.LlmModel})");
var persona = new Persona(chatClient, config.PromptDir, "live_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPath, config.RecentTweetsKeep);
var voicevox = new VoicevoxClient(config.VoicevoxUrl);

string? resolvedDeviceName = ResolveOutputDevice(config.OutputDeviceName, selectDevice);
if (resolvedDeviceName is null)
{
    return 1;
}
using var audioPlayer = new AudioPlayer(resolvedDeviceName);
Console.WriteLine($"出力デバイス: {audioPlayer.DeviceName}");

// Phase H: 文単位ストリーミング + 2キューTTS + 感情タグ + コメント選択
var styleMap = new EmotionStyleMap(config.SpeakerId, config.EmotionStyleIds);
var ttsPipeline = new TtsPipeline(new VoicevoxSynthesizer(voicevox), new AudioPlayerSink(audioPlayer));
var commentSelector = new CommentSelector();
Console.WriteLine(config.UseStreaming ? "発話: ストリーミング (文単位・2キューTTS)" : "発話: 一括生成 (--no-stream)");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 即時終了せず、ループを抜けて配信メモを保存する
    cts.Cancel();
};

using ICommentSource commentSource = youtubeMode
    ? new YouTubeCommentSource(videoId, config.YouTubeApiKey)
    : new ConsoleCommentSource();
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

        // 溜まっているコメントを広めに拾い、優先度 (初見・質問優先) で最大5件を選ぶ
        var drained = commentSource.Drain(50);
        var comments = commentSelector.Select(drained, 5);
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

        string? reply;
        if (config.UseStreaming)
        {
            // ストリーミング: トークン → 文分割 → 文ごとにフィルタ → 感情タグ分離 → 2キューで合成/再生を並行化
            var spoken = new StringBuilder();
            var segments = LiveSpeech.ToSegmentsAsync(
                persona.GenerateStreamAsync(history, maxTokens: 200, cts.Token),
                filter, styleMap, spoken, cts.Token);
            try
            {
                await ttsPipeline.RunAsync(segments, cts.Token);
            }
            catch (FilterViolationException ex)
            {
                // フィルタ違反時: 応答を破棄し、そのターンの履歴も破棄して次へ (既存仕様に準拠)
                Console.WriteLine($"[skip] {ex.Message}");
                history.RemoveAt(history.Count - 1);
                continue;
            }
            reply = spoken.ToString();
            if (reply.Length == 0)
            {
                // 発話内容が無かった (タグのみ等): 履歴を汚さず次へ
                history.RemoveAt(history.Count - 1);
                continue;
            }
        }
        else
        {
            // 旧経路: 一括生成 → フィルタ → 合成 → 再生
            reply = await persona.GenerateAsync(history, maxTokens: 200, cts.Token);
            if (!filter.IsSafe(reply))
            {
                Console.WriteLine($"[skip] フィルタに掛かった応答: {reply}");
                history.RemoveAt(history.Count - 1);
                continue;
            }
            byte[] wav = await voicevox.SynthesizeAsync(reply, config.SpeakerId, cts.Token);
            await audioPlayer.PlayWavAsync(wav, cts.Token);
        }

        history.Add(new ChatMessage("assistant", reply));
        topicsLog.Add(reply);
        Console.WriteLine($"{displayName}: {reply}");
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

// 出力デバイスを解決する。
// - forceSelect が true、または configuredFragment に一致するデバイスが無い場合、対話で選ばせる
// - null を返した場合は起動中止 (ユーザーが選ばなかった)
static string? ResolveOutputDevice(string configuredFragment, bool forceSelect)
{
    var names = AudioPlayer.GetOutputDeviceNames();
    if (names.Count == 0)
    {
        Console.WriteLine("利用可能な出力デバイスがありません。");
        return null;
    }

    bool found = names.Any(n => n.Contains(configuredFragment, StringComparison.OrdinalIgnoreCase));
    if (!forceSelect && found)
    {
        return configuredFragment;
    }

    if (!found)
    {
        Console.WriteLine($"'{configuredFragment}' を含む出力デバイスが見つかりません。デバイスを選択してください。");
    }
    else
    {
        Console.WriteLine("出力デバイスを選択してください。");
    }
    for (int i = 0; i < names.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {names[i]}");
    }
    Console.Write($"番号を入力 (Enter で '{configuredFragment}' を使用): ");
    string? choice = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(choice))
    {
        if (found)
        {
            return configuredFragment;
        }
        Console.WriteLine("デバイスが選択されなかったため中止します。");
        return null;
    }

    if (!int.TryParse(choice, out int number) || number < 1 || number > names.Count)
    {
        Console.WriteLine($"無効な番号です: {choice}");
        return null;
    }
    return names[number - 1];
}
