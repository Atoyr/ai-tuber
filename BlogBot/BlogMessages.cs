using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Medoz.AiTuber.Core;

namespace Medoz.BlogBot;

/// <summary>LLM 応答からパースした未検証の記事案 (検証は <see cref="BlogGenerator"/> が行う)</summary>
public record BlogDraft(
    string Title,
    string? Slug,
    string? Description,
    string? Kind,
    IReadOnlyList<string> Tags,
    string Body);

/// <summary>
/// ブログ記事生成用のコンテキスト組み立て・パース・markdown 組み立てヘルパー。
/// TweetMessages と同様、純粋関数として切り出してユニットテスト対象にする。
/// </summary>
public static partial class BlogMessages
{
    private static readonly string[] Weekdays = { "月", "火", "水", "木", "金", "土", "日" };

    private static readonly string[] ValidKinds = { "daily", "stream", "game", "announce" };

    private const string DateFormat = "yyyy-MM-dd HH:mm";
    private const int MaxTags = 3;

    [GeneratedRegex("^[a-z0-9-]{3,50}$")]
    private static partial Regex SlugRegex();

    /// <summary>記事生成用コンテキスト(時刻・配信メモ・直近ツイート・直近記事)を組み立てる</summary>
    public static string BuildContext(MemoryData memory, DateTime now)
    {
        int weekdayIndex = ((int)now.DayOfWeek + 6) % 7;

        var parts = new List<string>
        {
            $"現在日時: {now.ToString(DateFormat, CultureInfo.InvariantCulture)} ({Weekdays[weekdayIndex]}曜日)",
        };

        if (memory.StreamNotes.Count > 0)
        {
            StreamNote latest = memory.StreamNotes[^1];
            parts.Add($"直近の配信メモ ({latest.Date}): {latest.Summary}");
        }

        if (memory.RecentTweets.Count > 0)
        {
            string joined = string.Join("\n", memory.RecentTweets.Take(10).Select(t => $"- {t}"));
            parts.Add($"自分の直近ツイート(内容と矛盾しないこと):\n{joined}");
        }

        if (memory.RecentPosts.Count > 0)
        {
            string joined = string.Join("\n", memory.RecentPosts.Select(p => $"- {p.Date} {p.Title}"));
            parts.Add($"自分の直近のブログ記事(同じテーマ・同じタイトルの繰り返し禁止):\n{joined}");
        }

        parts.Add("この状況に合うブログ記事を1つ、指示されたJSON形式で生成してください。");
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// LLM 応答から記事案 JSON をパースする。コードフェンスは除去する。
    /// パース失敗・title/body の欠落や空文字なら null。
    /// </summary>
    public static BlogDraft? ParseDraft(string raw)
    {
        if (raw is null)
        {
            return null;
        }

        string cleaned = raw.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? title = GetString(root, "title");
            string? body = GetString(root, "body");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out JsonElement tagsElement)
                && tagsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tag in tagsElement.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String
                        && tag.GetString()?.Trim() is { Length: > 0 } value)
                    {
                        tags.Add(value);
                    }
                    if (tags.Count >= MaxTags)
                    {
                        break;
                    }
                }
            }

            return new BlogDraft(
                Title: title.Trim(),
                Slug: GetString(root, "slug")?.Trim(),
                Description: GetString(root, "description")?.Trim(),
                Kind: GetString(root, "kind")?.Trim(),
                Tags: tags,
                Body: body.Trim());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>slug が URL・ファイル名に使える形式 (半角英小文字/数字/ハイフン 3〜50字) か</summary>
    public static bool IsValidSlug(string? slug)
        => slug is not null && SlugRegex().IsMatch(slug);

    /// <summary>slug が不正なときの日付ベースのフォールバック</summary>
    public static string FallbackSlug(DateTimeOffset now) => $"post-{now:yyyyMMdd}";

    /// <summary>kind を正規化する。未知の値は "daily" にフォールバック</summary>
    public static string NormalizeKind(string? kind)
    {
        string lowered = kind?.Trim().ToLowerInvariant() ?? "";
        return ValidKinds.Contains(lowered) ? lowered : "daily";
    }

    /// <summary>
    /// frontmatter + 本文の markdown を組み立てる (ai-tuber-blogs の記事フォーマット契約)。
    /// title / description は YAML として安全なようにダブルクォートで囲む。
    /// </summary>
    public static string BuildMarkdown(BlogPost post)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append($"title: {YamlQuote(post.Title)}\n");
        sb.Append($"description: {YamlQuote(post.Description)}\n");
        sb.Append($"date: {post.Date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)}\n");
        sb.Append($"kind: {post.Kind}\n");
        sb.Append($"tags: [{string.Join(", ", post.Tags.Select(YamlQuote))}]\n");
        sb.Append("---\n\n");
        sb.Append(post.Body);
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>Python の len 相当。Rune(コードポイント)で数える (TweetMessages と同じ基準)</summary>
    public static int CountLength(string text) => text.EnumerateRunes().Count();

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    /// <summary>YAML のダブルクォート文字列にエスケープする。改行は空白に潰す</summary>
    private static string YamlQuote(string value)
    {
        string flattened = value.Replace("\r", " ").Replace("\n", " ");
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
