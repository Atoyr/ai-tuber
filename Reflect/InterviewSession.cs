using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.Reflect;

/// <summary>
/// 壁打ちセッション: インタビュアー(LLM)と開発者の対話履歴を管理し、
/// 最後に会話全体を character.md への提案 (ReflectionResult) にまとめる。
/// まとめは 生成 → JSONパース → 禁止ワード検査 で失敗したら再試行 (ReflectionGenerator と同じ方針)。
/// 失敗した試行の応答は履歴に残さない。
/// </summary>
public class InterviewSession
{
    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly List<ChatMessage> _history = new();
    private readonly int _replyMaxTokens;
    private readonly int _summaryMaxTokens;
    private readonly int _maxAttempts;

    public InterviewSession(Persona persona, ModerationFilter filter,
                            int replyMaxTokens = 500, int summaryMaxTokens = 1500, int maxAttempts = 3)
    {
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _replyMaxTokens = replyMaxTokens;
        _summaryMaxTokens = summaryMaxTokens;
        _maxAttempts = maxAttempts;
    }

    /// <summary>壁打ちを開始し、インタビュアーの最初の質問を返す</summary>
    public Task<string> StartAsync(string characterPrompt, MemoryData memory, DateTime now,
                                   CancellationToken ct = default)
        => ExchangeAsync(InterviewMessages.BuildOpening(characterPrompt, memory, now), ct);

    /// <summary>開発者の発言を送り、インタビュアーの応答を返す</summary>
    public Task<string> AskAsync(string userText, CancellationToken ct = default)
        => ExchangeAsync(userText, ct);

    private async Task<string> ExchangeAsync(string userText, CancellationToken ct)
    {
        _history.Add(new ChatMessage("user", userText));
        string reply = await _persona.GenerateAsync(_history, _replyMaxTokens, ct);
        _history.Add(new ChatMessage("assistant", reply));
        return reply;
    }

    /// <summary>
    /// ここまでの会話を提案JSONにまとめる。全試行が失敗した場合は null(=今回はスキップ)。
    /// </summary>
    public async Task<ReflectionResult?> SummarizeAsync(CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            var messages = new List<ChatMessage>(_history)
            {
                new("user", InterviewMessages.SummarizeRequest),
            };

            string raw = await _persona.GenerateAsync(messages, _summaryMaxTokens, ct);

            ReflectionResult? result = ReflectionMessages.Parse(raw);
            if (result is null)
            {
                string preview = raw.Length > 80 ? raw[..80] : raw;
                Console.WriteLine($"[retry] JSONパース失敗: {preview}");
                continue;
            }

            // 提案は character.md に反映されうるので、共通の禁止ワードフィルタを通す
            string? banned = ReflectionMessages.AllTexts(result)
                .Select(_filter.FindBannedWord)
                .FirstOrDefault(w => w is not null);
            if (banned is not null)
            {
                Console.WriteLine($"[retry] 禁止ワードを含む提案: {banned}");
                continue;
            }

            return result;
        }
        return null;
    }
}
