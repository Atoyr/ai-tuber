using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Medoz.AiTuber.Core;

namespace Medoz.Reflect;

/// <summary>
/// character.md の1つの見出しへの追記・修正提案。
/// <paramref name="Replaces"/> が null なら新規追記、非 null なら既存のその記述を <paramref name="Add"/> に置き換える提案。
/// </summary>
public record PersonaProposal(
    [property: JsonPropertyName("section")] string Section,
    [property: JsonPropertyName("add")] string Add,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("replaces")] string? Replaces = null);

/// <summary>振り返り1回分の結果(気づき + 人格提案)</summary>
public record ReflectionResult(
    [property: JsonPropertyName("observations")] IReadOnlyList<string> Observations,
    [property: JsonPropertyName("proposals")] IReadOnlyList<PersonaProposal> Proposals);

/// <summary>
/// 振り返り(人格育成提案)用のメッセージ組み立て・パース・出力整形のヘルパー。
/// 純粋関数として切り出しているのでユニットテストしやすい (TweetMessages と同じ方針)。
/// </summary>
public static class ReflectionMessages
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// 振り返り用コンテキストを組み立てる。
    /// 現在の character.md 本文(既出の判定用)+ 記憶(全配信メモ・直近ツイート・直近ブログ)を渡す。
    /// </summary>
    public static string BuildContext(MemoryData memory, string characterPrompt, DateTime now)
    {
        var parts = new List<string>
        {
            $"現在日時: {now.ToString(DateFormat, CultureInfo.InvariantCulture)}",
            "## 現在の人格設定 (character.md。ここに既にあることは提案しない)\n" + characterPrompt.Trim(),
        };

        if (memory.StreamNotes.Count > 0)
        {
            string joined = string.Join("\n", memory.StreamNotes.Select(n => $"- ({n.Date}) {n.Summary}"));
            parts.Add("## 配信メモ(古い順)\n" + joined);
        }

        if (memory.RecentTweets.Count > 0)
        {
            string joined = string.Join("\n", memory.RecentTweets.Select(t => $"- {t}"));
            parts.Add("## 直近ツイート(新しい順)\n" + joined);
        }

        if (memory.RecentPosts.Count > 0)
        {
            string joined = string.Join("\n", memory.RecentPosts.Select(p => $"- ({p.Date}) {p.Title}"));
            parts.Add("## 直近ブログ記事(新しい順)\n" + joined);
        }

        if (memory.StreamNotes.Count == 0 && memory.RecentTweets.Count == 0 && memory.RecentPosts.Count == 0)
        {
            parts.Add("(まだ記憶がありません。根拠が無いので proposals は空配列にしてください)");
        }

        parts.Add("これまでの記憶をもとに、指示されたJSON形式で振り返りと人格提案を出力してください。");
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Claude の応答から {"observations": [...], "proposals": [...]} をパースする。
    /// コードフェンス(```json / ```)は除去する。
    /// パース失敗・observations/proposals いずれかが欠落・型不一致なら null。
    /// </summary>
    public static ReflectionResult? Parse(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        string cleaned = raw.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            ReflectionResult? result = JsonSerializer.Deserialize<ReflectionResult>(cleaned);
            // observations / proposals が両方 JSON に存在することを要求する
            // (Deserialize は欠落キーを null にするため、ここで弾く)
            if (result?.Observations is null || result.Proposals is null)
            {
                return null;
            }
            // 各提案が空でないことを確認(section / add が空なら人格に足せない)
            if (result.Proposals.Any(p => string.IsNullOrWhiteSpace(p.Section) || string.IsNullOrWhiteSpace(p.Add)))
            {
                return null;
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>提案・気づきに含まれる全テキストを列挙する(フィルタ検査用)</summary>
    public static IEnumerable<string> AllTexts(ReflectionResult result)
    {
        foreach (string o in result.Observations)
        {
            yield return o;
        }
        foreach (PersonaProposal p in result.Proposals)
        {
            yield return p.Section;
            yield return p.Add;
            yield return p.Reason;
        }
    }

    /// <summary>
    /// 人間がレビューするための提案 Markdown を組み立てる。
    /// このファイルを読んで、採用するものだけを手で character.md に反映する。
    /// </summary>
    public static string RenderMarkdown(ReflectionResult result, string personaName, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 人格提案 ({now.ToString(DateFormat, CultureInfo.InvariantCulture)}) — {personaName}");
        sb.AppendLine();
        sb.AppendLine("記憶から生成した character.md への追記提案です。");
        sb.AppendLine("採用するものだけを手で character.md に反映してください(このツールは character.md を書き換えません)。");
        sb.AppendLine();

        sb.AppendLine("## 気づき(記憶から)");
        if (result.Observations.Count == 0)
        {
            sb.AppendLine("- (なし)");
        }
        else
        {
            foreach (string o in result.Observations)
            {
                sb.AppendLine($"- {o}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 提案");
        if (result.Proposals.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("今回は記憶から足すに値する提案はありませんでした。");
            return sb.ToString();
        }

        foreach (PersonaProposal p in result.Proposals)
        {
            sb.AppendLine();
            sb.AppendLine($"### {p.Section}");
            if (string.IsNullOrWhiteSpace(p.Replaces))
            {
                sb.AppendLine($"- **追記案**: {p.Add}");
            }
            else
            {
                sb.AppendLine($"- **修正案**: {p.Add}");
                sb.AppendLine($"- 置き換え対象: {p.Replaces}");
            }
            sb.AppendLine($"- 理由: {p.Reason}");
        }
        return sb.ToString();
    }
}
