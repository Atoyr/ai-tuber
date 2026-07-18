using Medoz.AiTuber.Core;
using Medoz.GameCommentary;
using Medoz.MultiLLMClient;
using Medoz.Voicevox;

// ゲーム実況パイプライン (Python版 game_commentary.py 相当)
// 特定ウィンドウを一定間隔でキャプチャ → Claude(Vision)で実況 → フィルタ
// → VOICEVOX → 仮想オーディオデバイス → PuruPuruPNGTuber口パク
//
// 実行:
//   dotnet run --project GameCommentary -- --window "VALORANT"      # 対象ウィンドウ指定
//   dotnet run --project GameCommentary -- "Minecraft"              # 位置引数でも可
//   dotnet run --project GameCommentary -- --window "..." --no-voice # 発話せずコンソール出力のみ
//   dotnet run --project GameCommentary -- --list-devices           # 出力デバイス一覧
//   dotnet run --project GameCommentary -- --window "..." --provider gemini
//   dotnet run --project GameCommentary -- --window "..." --capture-method printwindow # 従来方式
//   dotnet run --project GameCommentary -- --window "..." --game minecraft # knowledge/minecraft.md を実況コンテキストに使う
// ウィンドウは --window / 位置引数 / 環境変数 WINDOW_TITLE_FRAGMENT の順で解決する。
// キャプチャ方式は --capture-method / 環境変数 CAPTURE_METHOD の順 (既定 wgc)。
// ゲーム知識は --game / 環境変数 GAME_KNOWLEDGE の順 (未指定なら知識なし)。

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

bool noVoice = args.Contains("--no-voice");
bool selectDevice = args.Contains("--select-device");

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
}

// --window <title> で対象ウィンドウのタイトル(部分一致)を指定できる
string? windowArg = null;
int windowArgIndex = Array.IndexOf(args, "--window");
if (windowArgIndex >= 0)
{
    if (windowArgIndex + 1 >= args.Length)
    {
        Console.WriteLine("--window には対象ウィンドウタイトルの一部を指定してください。");
        return 1;
    }
    windowArg = args[windowArgIndex + 1];
}

// --game <name> で環境変数 GAME_KNOWLEDGE を上書きできる (knowledge/<name>.md をコンテキストに使う)
int gameArgIndex = Array.IndexOf(args, "--game");
if (gameArgIndex >= 0)
{
    if (gameArgIndex + 1 >= args.Length)
    {
        Console.WriteLine("--game にはペルソナの knowledge/<name>.md の <name> を指定してください。");
        return 1;
    }
    config = config with { GameKnowledge = args[gameArgIndex + 1] };
}

// --capture-method <wgc|printwindow> で環境変数 CAPTURE_METHOD を上書きできる
int captureMethodIndex = Array.IndexOf(args, "--capture-method");
if (captureMethodIndex >= 0)
{
    if (captureMethodIndex + 1 >= args.Length)
    {
        Console.WriteLine($"--capture-method には {WindowCaptureFactory.Wgc} / {WindowCaptureFactory.PrintWindow} のいずれかを指定してください。");
        return 1;
    }
    config = config with { CaptureMethod = args[captureMethodIndex + 1] };
}

string captureMethod;
try
{
    captureMethod = WindowCaptureFactory.NormalizeMethod(config.CaptureMethod);
}
catch (ArgumentException ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}

if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLower()))
{
    Console.WriteLine($"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai のいずれかを指定してください)");
    return 1;
}

// フラグの値として消費した引数を除いた位置引数
var consumed = new HashSet<string>();
foreach (var flag in new[] { "--provider", "--device", "--window", "--capture-method", "--game" })
{
    int idx = Array.IndexOf(args, flag);
    if (idx >= 0 && idx + 1 < args.Length)
    {
        consumed.Add(args[idx + 1]);
    }
}
string[] positional = args.Where(a => !a.StartsWith("--") && !consumed.Contains(a)).ToArray();

// ウィンドウタイトル: --window → 位置引数 → 環境変数 WINDOW_TITLE_FRAGMENT の順で解決
string windowTitle = windowArg
    ?? (positional.Length > 0 ? positional[0] : config.GameWindowTitle);
if (string.IsNullOrEmpty(windowTitle))
{
    Console.WriteLine("対象ウィンドウが未指定です。--window <タイトルの一部> か、環境変数 WINDOW_TITLE_FRAGMENT を設定してください。");
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
    personaPackage = PersonaPackage.Load(config.PersonaDir, "game_system.md");
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

// ゲーム知識 (knowledge/<name>.md) を指定されていればシステムプロンプトに結合する。
// 無い知識名なら利用可能一覧付きの例外で fail fast
string? gameKnowledge = string.IsNullOrEmpty(config.GameKnowledge) ? null : config.GameKnowledge;
Persona persona;
try
{
    persona = new Persona(chatClient, personaPackage, "game_system.md", gameKnowledge);
}
catch (PersonaLoadException ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}
if (gameKnowledge is not null)
{
    Console.WriteLine($"ゲーム知識: {gameKnowledge}");
}
var filter = new ModerationFilter(config.BannedWords);

// 対象ウィンドウを特定 (見つからなければ候補一覧付きで中止)
IWindowCapture capture;
try
{
    capture = WindowCaptureFactory.Create(captureMethod, windowTitle, config.MaxImageWidth);
    Console.WriteLine($"対象ウィンドウ: {capture.TargetTitle} (キャプチャ方式: {captureMethod})");
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}

// 発話の準備 (--no-voice ならコンソール出力のみ)
ISpeaker speaker;
VoicevoxSpeaker? voicevoxSpeaker = null;
if (noVoice)
{
    speaker = new NullSpeaker();
    Console.WriteLine("[no-voice] 発話せずコンソール出力のみで実行します。");
}
else
{
    string? resolvedDeviceName = ResolveOutputDevice(config.OutputDeviceName, selectDevice);
    if (resolvedDeviceName is null)
    {
        return 1;
    }
    var voicevox = new VoicevoxClient(config.VoicevoxUrl);
    var audioPlayer = new AudioPlayer(resolvedDeviceName);
    Console.WriteLine($"出力デバイス: {audioPlayer.DeviceName}");
    // Phase H: 感情タグ (character.md) を解釈して VOICEVOX スタイルを切り替える (Live と共通部品)
    var styleMap = new EmotionStyleMap(config.SpeakerId, config.EmotionStyleIds);
    voicevoxSpeaker = new VoicevoxSpeaker(voicevox, audioPlayer, config.SpeakerId, styleMap);
    speaker = voicevoxSpeaker;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 即時終了せず、ループを抜けて後始末する
    cts.Cancel();
};

var loop = new CommentaryLoop(capture, persona, filter, speaker, config.CommentaryHistoryLimit, displayName,
                              maxTokens: config.CommentaryMaxTokens);

Console.WriteLine("=== ゲーム実況ループ開始 (Ctrl+Cで終了) ===");
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var start = DateTime.UtcNow;

        await loop.RunOnceAsync(cts.Token);

        // Python版: 実処理時間を差し引いて CAPTURE_INTERVAL_SEC 間隔を保つ
        double elapsed = (DateTime.UtcNow - start).TotalSeconds;
        double wait = Math.Max(0, config.CaptureIntervalSec - elapsed);
        if (wait > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(wait), cts.Token);
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C による正常終了
}
finally
{
    voicevoxSpeaker?.Dispose();
    (capture as IDisposable)?.Dispose(); // WGC は D3D デバイスを持つ
    Console.WriteLine("ゲーム実況ループ終了");
}

return 0;

// 出力デバイスを解決する (Live/Program.cs と同じ挙動)。
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
