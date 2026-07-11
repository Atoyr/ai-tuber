using System.Runtime.CompilerServices;
using System.Text;

namespace Medoz.Voicevox;

/// <summary>
/// ストリーミングで届くトークン列を「文」単位に確定させる純粋な変換 (Phase H)。
/// 句点・感嘆符・疑問符 (全角/半角) を文の区切りとし、区切りが現れた時点でその文を確定して yield する。
/// ストリームが終わったら、末尾の未確定分 (区切り記号で終わらなかった残り) をフラッシュする。
/// これにより「確定した文から順次合成」して発話遅延を減らせる。
/// </summary>
public static class SentenceSplitter
{
    /// <summary>文末とみなす区切り文字 (全角/半角の句点・感嘆符・疑問符・改行)。</summary>
    private static readonly char[] Terminators = { '。', '!', '?', '!', '?', '\n' };

    /// <summary>
    /// トークン列を文単位に確定して流す。空文字・空白のみの文は捨てる。
    /// 末尾の未確定分は最後にフラッシュする。
    /// </summary>
    public static async IAsyncEnumerable<string> SplitAsync(
        IAsyncEnumerable<string> tokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = new StringBuilder();
        await foreach (string token in tokens.WithCancellation(ct))
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }
            foreach (char c in token)
            {
                buffer.Append(c);
                if (Array.IndexOf(Terminators, c) >= 0)
                {
                    string sentence = buffer.ToString().Trim();
                    buffer.Clear();
                    if (sentence.Length > 0)
                    {
                        yield return sentence;
                    }
                }
            }
        }

        string tail = buffer.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    /// <summary>
    /// 同期版 (テスト・非ストリーミング用途)。1本の文字列を文単位に分割する。
    /// </summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }
        var buffer = new StringBuilder();
        foreach (char c in text)
        {
            buffer.Append(c);
            if (Array.IndexOf(Terminators, c) >= 0)
            {
                string sentence = buffer.ToString().Trim();
                buffer.Clear();
                if (sentence.Length > 0)
                {
                    result.Add(sentence);
                }
            }
        }
        string tail = buffer.ToString().Trim();
        if (tail.Length > 0)
        {
            result.Add(tail);
        }
        return result;
    }
}
