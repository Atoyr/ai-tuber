using System.Text.RegularExpressions;

namespace Medoz.Voicevox;

/// <summary>感情タグのパース結果。<see cref="Emotion"/> はタグ無しなら空文字。</summary>
public readonly record struct EmotionParseResult(string Emotion, string Text);

/// <summary>
/// 発話テキスト先頭の感情タグ (例: <c>[joy]</c>) をパースして本文と分離する純粋クラス (Phase H)。
/// タグ規約はペルソナの character.md ($PERSONA_DIR) に定義。タグは本文から除去され、合成には本文のみを使う。
/// </summary>
public static partial class EmotionTagParser
{
    // 先頭の "[tag]" (英字のみ)。前後の空白は許容し、続く空白ごと除去する。
    [GeneratedRegex(@"^\s*\[(?<tag>[a-zA-Z]+)\]\s*")]
    private static partial Regex LeadingTagRegex();

    /// <summary>
    /// 先頭の感情タグを取り出し、本文からタグを除去して返す。
    /// タグが無ければ Emotion は空文字、Text はトリムした元テキスト。
    /// </summary>
    public static EmotionParseResult Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new EmotionParseResult(string.Empty, string.Empty);
        }

        Match m = LeadingTagRegex().Match(text);
        if (!m.Success)
        {
            return new EmotionParseResult(string.Empty, text.Trim());
        }

        string emotion = m.Groups["tag"].Value.ToLowerInvariant();
        string body = text[m.Length..].Trim();
        return new EmotionParseResult(emotion, body);
    }
}

/// <summary>
/// 感情タグ → VOICEVOX スタイルID のマッピング (Phase H)。
/// 未知タグ・タグ無しは既定スタイル (<see cref="DefaultStyleId"/>) にフォールバックする。
/// 既定マップは speaker 3 (ずんだもん) のスタイルを想定。AppConfig から差し替え可能。
/// </summary>
public sealed class EmotionStyleMap
{
    private readonly IReadOnlyDictionary<string, int> _map;

    public int DefaultStyleId { get; }

    public EmotionStyleMap(int defaultStyleId, IReadOnlyDictionary<string, int>? map = null)
    {
        DefaultStyleId = defaultStyleId;
        _map = map ?? new Dictionary<string, int>();
    }

    /// <summary>感情タグ (空文字/未知を含む) に対応するスタイルIDを返す。</summary>
    public int Resolve(string? emotion)
    {
        if (!string.IsNullOrEmpty(emotion) && _map.TryGetValue(emotion.ToLowerInvariant(), out int id))
        {
            return id;
        }
        return DefaultStyleId;
    }
}
