using Medoz.AiTuber.Core;
using Medoz.BlogBot;
using Medoz.MultiLLMClient;

// ブログ自律投稿ボット
// キャラクター人格でブログ記事 (markdown) を生成し、ai-tuber-blogs リポジトリの
// content/posts/ に commit & push する。main に push されると GitHub Actions が
// GitHub Pages へ自動公開する (設計: docs/blog-architecture.md)。
//
// 安全設計:
// - デフォルトは dry-run (push せず markdown をコンソール表示のみ)。BLOG_DRY_RUN=0 で本番化
// - タイトル40字・本文200〜2000字・禁止ワード・直近記事とのタイトル重複を検証し、
//   失敗したら再生成 (最大3回)、ダメなら投稿スキップ
//
// 実行 (1回生成して終了。定期実行はタスクスケジューラ等に任せる):
//   dotnet run --project BlogBot
//   dotnet run --project BlogBot -- --provider gemini

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

var persona = new Persona(chatClient, config.PromptDir, "blog_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPath, config.RecentTweetsKeep);
var generator = new BlogGenerator(persona, filter, memory);

IBlogPublisher publisher;
if (config.BlogDryRun)
{
    publisher = new DryRunBlogPublisher();
    Console.WriteLine("DRY RUN: 実 push しません (BLOG_DRY_RUN=0 で本番化)");
}
else
{
    publisher = new GitBlogPublisher(config.BlogRepoPath);
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

BlogPost? post = await generator.GenerateAsync(DateTimeOffset.Now, cts.Token);
if (post is null)
{
    Console.WriteLine("生成に3回失敗したため今回はスキップ");
    return 1;
}

await publisher.PublishAsync(post, cts.Token);
memory.AddPost(post.Title);
return 0;
