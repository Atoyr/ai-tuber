namespace Medoz.BlogBot;

/// <summary>
/// 検証済みのブログ記事1件。ai-tuber-blogs の記事フォーマット
/// (docs/blog-architecture.md の frontmatter 契約) に対応する。
/// </summary>
public record BlogPost(
    string Title,
    string Slug,
    string Description,
    string Kind,
    IReadOnlyList<string> Tags,
    string Body,
    DateTimeOffset Date)
{
    /// <summary>content/posts/ に置くファイル名 (例: 2026-07-12-first-stew.md)</summary>
    public string FileName => $"{Date:yyyy-MM-dd}-{Slug}.md";
}
