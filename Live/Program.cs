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
//   dotnet run --project Live -- --twitch <channel|URL>            # Twitch チャットを取得 (APIキー不要)
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

// --twitch <channel|URL> で Twitch チャットのコメントを取得する (匿名接続・APIキー不要)
string? twitchChannelArg = null;
int twitchArgIndex = Array.IndexOf(args, "--twitch");
if (twitchArgIndex >= 0)
{
    if (twitchArgIndex + 1 >= args.Length)
    {
        Console.WriteLine("--twitch にはチャンネル名または twitch.tv の URL を指定してください。");
        return 1;
    }
    twitchChannelArg = args[twitchArgIndex + 1];
    positional = positional.Where(a => a != twitchChannelArg).ToArray();
}
bool twitchMode = twitchChannelArg is not null;

if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLower()))
{
    Console.WriteLine($"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai のいずれかを指定してください)");
    return 1;
}

// videoId は --youtube の値 → 位置引数 → 環境変数 YT_VIDEO_ID の順で解決する
string videoId = youtubeVideoArg
    ?? (positional.Length > 0 ? positional[0] : config.YouTubeVideoId);

if (!consoleMode && !youtubeMode && !twitchMode && string.IsNullOrEmpty(videoId))
{
    Console.WriteLine("YT_VIDEO_ID が未設定です。--console でローカルテスト、--youtube <videoId|URL>、または --twitch <channel|URL> を指定してください。");
    return 1;
}
// videoId が環境変数から取れていれば --youtube 省略でも YouTube モードにする (Twitch/コンソール指定時は除く)
if (!consoleMode && !twitchMode && !string.IsNullOrEmpty(videoId))
{
    youtubeMode = true;
}
if (!consoleMode && !youtubeMode && !twitchMode)
{
    Console.WriteLine("--console でローカルテスト、--youtube <videoId|URL>、または --twitch <channel|URL> を指定してください。");
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

// ペルソナパッケージ (人格・声・追加禁止ワード) をロードする。不足があればここで fail fast
PersonaPackage personaPackage;
try
{
    personaPackage = PersonaPackage.Load(config.PersonaDir, "live_system.md");
}
catch (PersonaLoadException ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}
config = config.ApplyPersona(personaPackage.Manifest);
Console.WriteLine($"ペルソナ: {personaPackage.Manifest.Name} ({config.PersonaDir})");

// キャラの表示名は persona.json の name (環境変数 CHARACTER_NAME で上書き可)
string displayName = Environment.GetEnvironmentVariable("CHARACTER_NAME") is { Length: > 0 } name ? name : personaPackage.Manifest.Name;

IChatClient chatClient = LLMClientFactory.CreateChatClient(config.LlmProvider, config.LlmApiKey, config.LlmModel);
Console.WriteLine($"LLM: {config.LlmProvider} ({config.LlmModel})");
var persona = new Persona(chatClient, personaPackage, "live_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPathFor(personaPackage.Manifest.Slug), config.RecentTweetsKeep);
var voicevox = new VoicevoxClient(config.VoicevoxUrl);

string? resolvedDeviceName = ResolveOutputDevice(config.OutputDeviceName, selectDevice);
if (resolvedDeviceName is null)
{
    return 1;
}
using var audioPlayer = new AudioPlayer(resolvedDeviceName);
Console.WriteLine($"出力デバイス: {audioPlayer.DeviceName}");

// Phase H: 文単位ストリーミング + 2キューTTS + 感情タグ + コメント選択
Console.WriteLine(config.UseStreaming ? "発話: ストリーミング (文単位・2キューTTS)" : "発話: 一括生成 (--no-stream)");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 即時終了せず、ループを抜けて配信メモを保存する
    cts.Cancel();
};

ICommentSource commentSource = twitchMode
    ? new TwitchCommentSource(twitchChannelArg!)
    : youtubeMode
        ? new YouTubeCommentSource(videoId, config.YouTubeApiKey)
        : new ConsoleCommentSource();

// 配信ループ本体は LiveSession に集約 (CLI と Studio で共用)。
// ここは「イベント → コンソール出力」の薄い CLI として従来の表示・体験を維持する。
var options = new LiveSessionOptions
{
    CommentBatchSec = config.CommentBatchSec,
    FreetalkAfterSec = config.FreetalkAfterSec,
    SpeakerId = config.SpeakerId,
    EmotionStyleIds = config.EmotionStyleIds,
};
await using var session = new LiveSession(
    options, commentSource, persona, filter, memory, voicevox, audioPlayer,
    config.HistoryTurns, config.UseStreaming);

session.EventRaised += liveEvent =>
{
    switch (liveEvent)
    {
        case SessionStateChanged { State: SessionState.Running }:
            Console.WriteLine("=== 配信ループ開始 (Ctrl+Cで終了) ===");
            break;
        case ReplySpoken spoken:
            Console.WriteLine($"{displayName}: {spoken.Text}");
            break;
        case ReplySkipped skipped:
            Console.WriteLine($"[skip] {skipped.Reason}");
            break;
        case StreamNoteSaved note:
            Console.WriteLine($"配信メモ保存: {note.Summary}");
            break;
        case SessionStateChanged { State: SessionState.Stopped or SessionState.Faulted }:
            Console.WriteLine("配信ループ終了");
            break;
    }
};

await session.RunAsync(cts.Token);

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
