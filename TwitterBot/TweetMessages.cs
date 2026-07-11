using System.Globalization;
using System.Text.Json;
using Medoz.AiTuber.Core;

namespace Medoz.TwitterBot;

/// <summary>
/// ツイート生成用のメッセージ組み立て・パースヘルパー (Python版 twitter_bot.py 相当)。
/// 純粋関数として切り出しているのでユニットテストしやすい。
/// </summary>
public static class TweetMessages
{
    // Python版: ['月','火','水','木','金','土','日'][now.weekday()] (月曜=0)
    private static readonly string[] Weekdays = { "月", "火", "水", "木", "金", "土", "日" };

    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>ツイート生成用コンテキスト(時刻・配信メモ・直近ツイート)を組み立てる (build_context 相当)</summary>
    public static string BuildContext(MemoryData memory, DateTime now)
    {
        // .NET の DayOfWeek は日曜=0。Python weekday() の月曜=0 に合わせる
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
            parts.Add($"自分の直近ツイート(繰り返し禁止):\n{joined}");
        }

        parts.Add("この状況に合うツイートを1つ、指示されたJSON形式で生成してください。");
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Claude の応答から {"tweet": "...", "kind": "..."} をパースして本文を返す。
    /// コードフェンス(```json / ```)は Python版と同様に除去する。
    /// パース失敗・tweet キー欠落・非文字列なら null。
    /// </summary>
    public static string? ParseTweet(string raw)
    {
        if (raw is null)
        {
            return null;
        }

        // Python版: raw.replace("```json", "").replace("```", "").strip()
        string cleaned = raw.Replace("```json", "").Replace("```", "").Trim();

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (!doc.RootElement.TryGetProperty("tweet", out JsonElement tweetElement)
                || tweetElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            return tweetElement.GetString()?.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 文字数を数える。Python の len(str)(= Unicode コードポイント数)に合わせるため
    /// UTF-16 コード単位ではなく Rune(コードポイント)で数える。絵文字は1文字扱い。
    /// </summary>
    public static int CountLength(string text) => text.EnumerateRunes().Count();
}
