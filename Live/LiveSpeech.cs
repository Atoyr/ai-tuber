using System.Runtime.CompilerServices;
using System.Text;
using Medoz.AiTuber.Core;
using Medoz.Voicevox;

namespace Medoz.Live;

/// <summary>フィルタ違反の文が現れたときに投げる。応答全体を中止し、そのターンの履歴を破棄する合図。</summary>
public sealed class FilterViolationException : Exception
{
    public string Sentence { get; }

    public FilterViolationException(string sentence)
        : base($"フィルタに掛かった応答: {sentence}")
        => Sentence = sentence;
}

/// <summary>
/// ストリーミング応答を「文単位 → フィルタ → 感情タグ分離 → 合成セグメント」に変換する (Phase H)。
/// - 文はフィルタチェックしてから合成する。違反があれば <see cref="FilterViolationException"/> で全体中止。
/// - 感情タグは本文から除去し、スタイルIDに変換する。先頭文のタグは以降のタグ無し文にも引き継ぐ。
/// - 実際に発話した本文は <paramref name="spokenText"/> に蓄積する (履歴・配信メモ用)。
/// </summary>
public static class LiveSpeech
{
    public static async IAsyncEnumerable<SpeechSegment> ToSegmentsAsync(
        IAsyncEnumerable<string> tokens,
        ModerationFilter filter,
        EmotionStyleMap styleMap,
        StringBuilder spokenText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string currentEmotion = string.Empty;

        await foreach (string sentence in SentenceSplitter.SplitAsync(tokens, ct))
        {
            if (!filter.IsSafe(sentence))
            {
                throw new FilterViolationException(sentence);
            }

            EmotionParseResult parsed = EmotionTagParser.Parse(sentence);
            if (!string.IsNullOrEmpty(parsed.Emotion))
            {
                currentEmotion = parsed.Emotion; // 先頭文で決めた感情を以降のタグ無し文にも適用する
            }
            if (parsed.Text.Length == 0)
            {
                continue; // タグだけの文 (読み上げる本文が無い) はスキップ
            }

            int styleId = styleMap.Resolve(currentEmotion);
            spokenText.Append(parsed.Text);
            yield return new SpeechSegment(parsed.Text, styleId);
        }
    }
}
