using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;
using Medoz.Reflect;

// 人格育成の振り返りツール。
// キャラクターが自分の記憶(配信メモ・ツイート・ブログ)を読み返し、
// character.md への追記提案を生成する。提案は data/<slug>/persona-proposals/ に Markdown で出るだけで、
// character.md は書き換えない(採用するかは人間が判断する = dry-run 原則)。
//
// 実行:
//   dotnet run --project Reflect                    # 提案を data/<slug>/persona-proposals/ に保存
//   dotnet run --project Reflect -- --print         # 保存せずコンソールに表示するだけ
//   dotnet run --project Reflect -- --provider gemini

var config = AppConfig.LoadFromEnvironment();

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

// ペルソナパッケージをロードする。reflect_system.md が無ければここで fail fast
PersonaPackage personaPackage;
try
{
    personaPackage = PersonaPackage.Load(config.PersonaDir, "reflect_system.md");
}
catch (PersonaLoadException ex)
{
    Console.WriteLine(ex.Message);
    return 1;
}
config = config.ApplyPersona(personaPackage.Manifest);
Console.WriteLine($"ペルソナ: {personaPackage.Manifest.Name} ({config.PersonaDir})");

IChatClient chatClient = LLMClientFactory.CreateChatClient(config.LlmProvider, config.LlmApiKey, config.LlmModel);
Console.WriteLine($"LLM: {config.LlmProvider} ({config.LlmModel})");

var persona = new Persona(chatClient, personaPackage, "reflect_system.md");
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPathFor(personaPackage.Manifest.Slug));
var generator = new ReflectionGenerator(persona, filter, memory, personaPackage.CharacterPrompt);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

DateTime now = DateTime.Now;
ReflectionResult? result;
try
{
    result = await generator.GenerateAsync(now, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("中断しました。");
    return 0;
}

if (result is null)
{
    Console.WriteLine("生成に3回失敗したため今回はスキップ");
    return 1;
}

string markdown = ReflectionMessages.RenderMarkdown(result, personaPackage.Manifest.Name, now);

IProposalWriter writer = args.Contains("--print")
    ? new StdoutProposalWriter()
    : new FileProposalWriter(Path.Combine(config.DataDir, personaPackage.Manifest.Slug, "persona-proposals"));

string location = writer.Write(markdown, now);
Console.WriteLine($"提案 {result.Proposals.Count} 件 / 気づき {result.Observations.Count} 件 → {location}");
return 0;
