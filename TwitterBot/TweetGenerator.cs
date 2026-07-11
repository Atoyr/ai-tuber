using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.TwitterBot;

/// <summary>
/// ツイート本文を生成する (Python版 twitter_bot.py の generate_tweet 相当)。
/// 生成 → JSONパース → 140字/フィルタ/完全重複 の検証を行い、
/// 失敗したら再生成 (最大 maxAttempts 回)。全部ダメなら null を返す(=今回はスキップ)。
/// LLM 呼び出しは <see cref="Persona"/>(IChatClient ラッパ)経由なのでフェイク注入でテストできる。
/// </summary>
public class TweetGenerator
{
    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly SharedMemory _memory;
    private readonly int _maxLength;
    private readonly int _maxAttempts;

    public TweetGenerator(Persona persona, ModerationFilter filter, SharedMemory memory,
                          int maxLength = 140, int maxAttempts = 3)
    {
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _maxLength = maxLength;
        _maxAttempts = maxAttempts;
    }

    /// <summary>生成に成功したツイート本文、または全試行が失敗した場合は null</summary>
    public async Task<string?> GenerateAsync(DateTime now, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            // Python版と同様、毎回メモリを読み直してコンテキストと重複判定に使う
            MemoryData memory = _memory.Load();
            var messages = new List<ChatMessage>
            {
                new("user", TweetMessages.BuildContext(memory, now)),
            };

            string raw = await _persona.GenerateAsync(messages, maxTokens: 300, ct);

            string? text = TweetMessages.ParseTweet(raw);
            if (text is null)
            {
                string preview = raw.Length > 80 ? raw[..80] : raw;
                Console.WriteLine($"[retry] JSONパース失敗: {preview}");
                continue;
            }
            if (TweetMessages.CountLength(text) > _maxLength)
            {
                Console.WriteLine($"[retry] {_maxLength}字超過: {TweetMessages.CountLength(text)}字");
                continue;
            }
            if (!_filter.IsSafe(text))
            {
                Console.WriteLine($"[retry] フィルタに掛かった: {text}");
                continue;
            }
            if (memory.RecentTweets.Contains(text))
            {
                Console.WriteLine("[retry] 完全重複");
                continue;
            }
            return text;
        }
        return null;
    }
}
