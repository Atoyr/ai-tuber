using Medoz.AiTuber.Core;
using Medoz.MultiLLMClient;

namespace Medoz.BlogBot;

/// <summary>
/// ブログ記事を生成する (TweetGenerator と同じ構造)。
/// 生成 → JSONパース → タイトル字数/本文字数/フィルタ/タイトル重複 の検証を行い、
/// 失敗したら再生成 (最大 maxAttempts 回)。全部ダメなら null を返す(=今回はスキップ)。
/// </summary>
public class BlogGenerator
{
    public const int TitleMaxLength = 40;
    public const int BodyMinLength = 200;
    public const int BodyMaxLength = 2000;

    private readonly Persona _persona;
    private readonly ModerationFilter _filter;
    private readonly SharedMemory _memory;
    private readonly int _maxAttempts;

    public BlogGenerator(Persona persona, ModerationFilter filter, SharedMemory memory, int maxAttempts = 3)
    {
        _persona = persona ?? throw new ArgumentNullException(nameof(persona));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _maxAttempts = maxAttempts;
    }

    /// <summary>検証を通った記事、または全試行が失敗した場合は null</summary>
    public async Task<BlogPost?> GenerateAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            MemoryData memory = _memory.Load();
            var messages = new List<ChatMessage>
            {
                new("user", BlogMessages.BuildContext(memory, now.LocalDateTime)),
            };

            // 本文800字 + frontmatter 分の余裕を見て多めに確保する
            string raw = await _persona.GenerateAsync(messages, maxTokens: 2000, ct);

            BlogDraft? draft = BlogMessages.ParseDraft(raw);
            if (draft is null)
            {
                string preview = raw.Length > 80 ? raw[..80] : raw;
                Console.WriteLine($"[retry] JSONパース失敗: {preview}");
                continue;
            }
            if (BlogMessages.CountLength(draft.Title) > TitleMaxLength)
            {
                Console.WriteLine($"[retry] タイトル{TitleMaxLength}字超過: {draft.Title}");
                continue;
            }
            int bodyLength = BlogMessages.CountLength(draft.Body);
            if (bodyLength < BodyMinLength || bodyLength > BodyMaxLength)
            {
                Console.WriteLine($"[retry] 本文が{BodyMinLength}〜{BodyMaxLength}字の範囲外: {bodyLength}字");
                continue;
            }
            string description = draft.Description is { Length: > 0 } d ? d : draft.Title;
            if (!_filter.IsSafe(draft.Title) || !_filter.IsSafe(description) || !_filter.IsSafe(draft.Body))
            {
                Console.WriteLine($"[retry] フィルタに掛かった: {draft.Title}");
                continue;
            }
            if (memory.RecentPosts.Any(p => p.Title == draft.Title))
            {
                Console.WriteLine("[retry] タイトルが直近記事と重複");
                continue;
            }

            string slug = BlogMessages.IsValidSlug(draft.Slug)
                ? draft.Slug!
                : BlogMessages.FallbackSlug(now);

            return new BlogPost(
                Title: draft.Title,
                Slug: slug,
                Description: description,
                Kind: BlogMessages.NormalizeKind(draft.Kind),
                Tags: draft.Tags,
                Body: draft.Body,
                Date: now);
        }
        return null;
    }
}
