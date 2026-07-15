using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.Reflect;

/// <summary>
/// 記憶から人格提案を生成する (TweetGenerator と同じ設計)。
/// 生成 → JSONパース → 禁止ワード検査 を行い、失敗したら再生成 (最大 maxAttempts 回)。
/// 全部ダメなら null を返す(=今回はスキップ)。
/// LLM 呼び出しは <see cref="Persona"/>(IChatClient ラッパ)経由なのでフェイク注入でテストできる。
/// </summary>
public class ReflectionGenerator
{
    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly SharedMemory _memory;
    private readonly string _characterPrompt;
    private readonly int _maxTokens;
    private readonly int _maxAttempts;

    public ReflectionGenerator(Persona persona, ModerationFilter filter, SharedMemory memory,
                               string characterPrompt, int maxTokens = 1000, int maxAttempts = 3)
    {
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _characterPrompt = characterPrompt ?? throw new ArgumentNullException(nameof(characterPrompt));
        _maxTokens = maxTokens;
        _maxAttempts = maxAttempts;
    }

    /// <summary>生成に成功した振り返り結果、または全試行が失敗した場合は null</summary>
    public async Task<ReflectionResult?> GenerateAsync(DateTime now, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            MemoryData memory = _memory.Load();
            var messages = new List<ChatMessage>
            {
                new("user", ReflectionMessages.BuildContext(memory, _characterPrompt, now)),
            };

            string raw = await _persona.GenerateAsync(messages, _maxTokens, ct);

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
