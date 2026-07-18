using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;
using Medoz.Reflect;

// 人格育成の振り返り・壁打ちツール。
// character.md への追記・修正提案を生成する。提案は data/<slug>/persona-proposals/ に Markdown で出るだけで、
// character.md は書き換えない(採用するかは人間が判断する = dry-run 原則)。
//
// 実行:
//   dotnet run --project Reflect                    # 記憶(配信メモ等)から提案を生成して保存
//   dotnet run --project Reflect -- --print         # 保存せずコンソールに表示するだけ
//   dotnet run --project Reflect -- --interview     # 壁打ち: AIの質問に答えて人格を掘り下げ、/done で提案化
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

bool interviewMode = args.Contains("--interview");
string modeFile = interviewMode ? "interview_system.md" : "reflect_system.md";

// ペルソナパッケージをロードする。モードの md が無ければここで fail fast
PersonaPackage personaPackage;
try
{
    personaPackage = PersonaPackage.Load(config.PersonaDir, modeFile);
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

var persona = new Persona(chatClient, personaPackage, modeFile);
var filter = new ModerationFilter(config.BannedWords);
var memory = new SharedMemory(config.MemoryPathFor(personaPackage.Manifest.Slug));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

IProposalWriter writer = args.Contains("--print")
    ? new StdoutProposalWriter()
    : new FileProposalWriter(Path.Combine(config.DataDir, personaPackage.Manifest.Slug, "persona-proposals"));

DateTime now = DateTime.Now;
ReflectionResult? result;
try
{
    result = interviewMode
        ? await RunInterviewAsync(persona, filter, memory, personaPackage.CharacterPrompt, now, cts.Token)
        : await new ReflectionGenerator(persona, filter, memory, personaPackage.CharacterPrompt)
              .GenerateAsync(now, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("中断しました。");
    return 0;
}

if (result is null)
{
    if (!interviewMode)
    {
        Console.WriteLine("生成に3回失敗したため今回はスキップ");
    }
    // 壁打ちの /quit(保存せず終了)は正常終了扱い
    return interviewMode ? 0 : 1;
}

string markdown = ReflectionMessages.RenderMarkdown(result, personaPackage.Manifest.Name, now);
string location = writer.Write(markdown, now);
Console.WriteLine($"提案 {result.Proposals.Count} 件 / 気づき {result.Observations.Count} 件 → {location}");
return 0;

// 壁打ちループ: AI が質問し、開発者が答える。/done でここまでの会話を提案にまとめて返す。
// /quit・入力終了(Ctrl+C 等)は null を返し、何も保存しない。
static async Task<ReflectionResult?> RunInterviewAsync(
    Persona persona, ModerationFilter filter, SharedMemory memory,
    string characterPrompt, DateTime now, CancellationToken ct)
{
    Console.WriteLine();
    Console.WriteLine("壁打ちモード: AI の質問に答えてキャラクターを掘り下げます。");
    Console.WriteLine("  /done … ここまでの会話から人格提案をまとめて保存して終了");
    Console.WriteLine("  /quit … 保存せず終了");
    Console.WriteLine();

    var session = new InterviewSession(persona, filter);

    string question = await session.StartAsync(characterPrompt, memory.Load(), now, ct);
    Console.WriteLine($"[壁打ち] {question}");

    while (true)
    {
        Console.Write("> ");
        string? input = Console.ReadLine();
        ct.ThrowIfCancellationRequested();

        if (input is null || input.Trim() == "/quit")
        {
            Console.WriteLine("保存せず終了します。");
            return null;
        }
        if (string.IsNullOrWhiteSpace(input))
        {
            continue;
        }
        if (input.Trim() == "/done")
        {
            Console.WriteLine("会話を提案にまとめています…");
            ReflectionResult? result = await session.SummarizeAsync(ct);
            if (result is null)
            {
                Console.WriteLine("まとめの生成に3回失敗したため保存をスキップします。");
            }
            return result;
        }

        string reply = await session.AskAsync(input, ct);
        Console.WriteLine($"[壁打ち] {reply}");
    }
}
