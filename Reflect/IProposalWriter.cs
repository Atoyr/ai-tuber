namespace Medoz.Reflect;

/// <summary>
/// 生成した人格提案 Markdown の出力先。
/// このツールは character.md を書き換えず、提案を人間のレビュー用に出すだけ
/// (「AIが提案し、人間が採用する」= dry-run 原則そのもの)。
/// </summary>
public interface IProposalWriter
{
    /// <summary>提案 Markdown を書き出し、参照先(パス等)を返す</summary>
    string Write(string markdown, DateTime now);
}

/// <summary>提案を data/&lt;slug&gt;/persona-proposals/&lt;timestamp&gt;.md に保存する</summary>
public sealed class FileProposalWriter : IProposalWriter
{
    private readonly string _directory;

    public FileProposalWriter(string directory)
    {
        _directory = directory;
    }

    public string Write(string markdown, DateTime now)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, now.ToString("yyyyMMdd-HHmmss") + ".md");
        File.WriteAllText(path, markdown);
        return path;
    }
}

/// <summary>提案をコンソールに表示するだけ(--print。ファイルを残さず内容だけ見たいとき)</summary>
public sealed class StdoutProposalWriter : IProposalWriter
{
    public string Write(string markdown, DateTime now)
    {
        Console.WriteLine();
        Console.WriteLine(markdown);
        return "(標準出力)";
    }
}
