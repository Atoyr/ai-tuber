using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;
using Medoz.TwitterBot;

// Twitter(X) 自律投稿ボット (Python版 twitter_bot.py 相当)
// キャラクター人格でツイートを生成し、ランダムな間隔で投稿する。
//
// 安全設計:
// - デフォルトは dry-run (実投稿せずコンソール表示のみ)。TWEET_DRY_RUN=0 で本番化
// - 直近ツイート履歴をコンテキストに渡して重複を回避
// - 140字・禁止ワード・完全重複を検証し、失敗したら再生成 (最大3回)、ダメなら投稿スキップ
// - 投稿時間帯 (9〜24時) と間隔 (180〜360分) を制限
//
// 実行:
//   dotnet run --project TwitterBot                 # 常駐してスケジュール投稿
//   dotnet run --project TwitterBot -- --once       # 1回だけ生成・投稿して終了 (動作確認用・時間帯チェックなし)
//   dotnet run --project TwitterBot -- --scheduled  # 時間帯 (9〜24時) 内なら1回投稿して終了 (systemd timer 用)
//   dotnet run --project TwitterBot -- --provider gemini

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

if (!new[] { "claude", "gemini", "openai" }.Contains(config.LlmProvider.ToLower()))
{
    Console.WriteLine($"未知の LLM プロバイダです: {config.LlmProvider} (claude / gemini / openai のいずれかを指定してください)");
    return 1;
}

if (string.IsNullOrEmpty(config.LlmApiKey))
{
    Console.WriteLine($"環境変数 {config.LlmApiKeyEnvName} が未設定です (LLM_PROVIDER={config.LlmProvider})");
    return 1;
}

IChatClient chatClient = LLMClientFactory.CreateChatClient(config.LlmProvider, config.LlmApiKey, config.LlmModel);
Console.WriteLine($"LLM: {config.LlmProvider} ({config.LlmModel})");

var persona = new Persona(chatClient, config.PromptDir, "tweet_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPath, config.RecentTweetsKeep);
var generator = new TweetGenerator(persona, filter, memory);

ITweetPoster poster;
if (config.TweetDryRun)
{
    poster = new DryRunTweetPoster();
}
else
{
    if (string.IsNullOrEmpty(config.XApiKey) || string.IsNullOrEmpty(config.XApiSecret)
        || string.IsNullOrEmpty(config.XAccessToken))
    {
        Console.WriteLine("実投稿には X_API_KEY / X_API_SECRET / X_ACCESS_TOKEN が必要です。");
        return 1;
    }
    poster = XTweetPoster.FromConfig(config);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 即時終了せず、待機ループを抜ける
    cts.Cancel();
};

if (args.Contains("--once"))
{
    await PostOnceAsync();
    return 0;
}

// systemd timer からの起動用: 時間帯内なら1回投稿、時間外なら何もせず正常終了
// (常駐ループの1イテレーションと同じ挙動。docs/twitterbot-linux-deployment.md 参照)
if (args.Contains("--scheduled"))
{
    if (TweetScheduler.InActiveHours(DateTime.Now, config.TweetActiveHourStart, config.TweetActiveHourEnd))
    {
        await PostOnceAsync();
    }
    else
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm}] 投稿時間外のためスキップ");
    }
    return 0;
}

Console.WriteLine("=== Twitter自律投稿ボット起動 (Ctrl+Cで終了) ===");
Console.WriteLine($"DRY RUN: {config.TweetDryRun} / 間隔: {config.TweetMinIntervalMin}〜{config.TweetMaxIntervalMin}分");

var random = new Random();
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        if (TweetScheduler.InActiveHours(DateTime.Now, config.TweetActiveHourStart, config.TweetActiveHourEnd))
        {
            await PostOnceAsync();
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm}] 投稿時間外のためスキップ");
        }

        int waitMin = TweetScheduler.NextIntervalMinutes(random, config.TweetMinIntervalMin, config.TweetMaxIntervalMin);
        Console.WriteLine($"次の投稿まで約{waitMin}分待機...");
        await Task.Delay(TimeSpan.FromMinutes(waitMin), cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C による正常終了
}
Console.WriteLine("Twitter自律投稿ボット終了");
return 0;

// 1回だけ生成して投稿する (Python版 post_once 相当)。生成に3回失敗したらスキップ。
async Task PostOnceAsync()
{
    string? text = await generator.GenerateAsync(DateTime.Now, cts.Token);
    if (text is null)
    {
        Console.WriteLine("生成に3回失敗したため今回はスキップ");
        return;
    }

    await poster.PostAsync(text, cts.Token);
    memory.AddTweet(text);
}
