using System.Globalization;
using System.Text;
using Medoz.AiTuber.Core;

namespace Medoz.Reflect;

/// <summary>
/// 壁打ち(対話的な人格生成・補正)用のメッセージ組み立てヘルパー。
/// 純粋関数として切り出しているのでユニットテストしやすい (ReflectionMessages と同じ方針)。
/// </summary>
public static class InterviewMessages
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>壁打ちの話題として渡す直近の配信メモ件数</summary>
    public const int RecentStreamNotes = 5;

    /// <summary>/done で送る、会話を提案JSONにまとめさせる指示</summary>
    public const string SummarizeRequest =
        "ここまでの会話をまとめてください。会話で確認できたことだけを、指示されたJSON形式のみで出力してください。";

    /// <summary>
    /// 壁打ちの最初のメッセージを組み立てる。
    /// 現在の character.md 本文(既出の判定用)+ 直近の配信メモ(話題の種)を渡し、最初の質問を促す。
    /// </summary>
    public static string BuildOpening(string characterPrompt, MemoryData memory, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"現在日時: {now.ToString(DateFormat, CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("## 現在の人格設定 (character.md。既に書いてあることは聞き直さない)");
        sb.AppendLine(characterPrompt.Trim());

        if (memory.StreamNotes.Count > 0)
        {
            var recent = memory.StreamNotes.TakeLast(RecentStreamNotes);
            sb.AppendLine();
            sb.AppendLine("## 直近の配信メモ(話題の参考に)");
            foreach (StreamNote note in recent)
            {
                sb.AppendLine($"- ({note.Date}) {note.Summary}");
            }
        }

        sb.AppendLine();
        sb.Append("壁打ちを始めます。人格設定を掘り下げるための最初の質問をしてください。");
        return sb.ToString();
    }
}
