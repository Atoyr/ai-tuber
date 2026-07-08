namespace Medoz.AiTuber.Core;

/// <summary>
/// 禁止ワードフィルタ。Live / Twitter / GameCommentary で共通に使う。
/// 引っかかった応答は破棄して作り直す (Python版 persona.is_safe 相当)。
/// </summary>
public class ModerationFilter
{
    private readonly IReadOnlyList<string> _bannedWords;

    public ModerationFilter(IEnumerable<string> bannedWords)
    {
        _bannedWords = bannedWords?.ToArray() ?? throw new ArgumentNullException(nameof(bannedWords));
    }

    /// <summary>禁止ワードを含まなければ true</summary>
    public bool IsSafe(string? text)
        => FindBannedWord(text) is null;

    /// <summary>テキストに含まれる最初の禁止ワードを返す。無ければ null (ログ出力用)</summary>
    public string? FindBannedWord(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return _bannedWords.FirstOrDefault(w => text.Contains(w, StringComparison.Ordinal));
    }
}
