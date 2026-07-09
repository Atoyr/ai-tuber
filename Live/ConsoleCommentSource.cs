using System.Collections.Concurrent;

namespace Medoz.Live;

/// <summary>
/// 配信なしローカルテスト用: コンソール入力をコメントとして流し込む
/// (Python版 comment_worker_console 相当)。
/// </summary>
public class ConsoleCommentSource : ICommentSource
{
    private const string Author = "テスト太郎";

    private readonly ConcurrentQueue<Comment> _queue = new();
    private Task? _worker;

    public void Start(CancellationToken ct)
    {
        Console.WriteLine("コンソールモード: コメント役のテキストを入力してください (Ctrl+Cで終了)");
        _worker = Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = Console.ReadLine();
                if (line is null)
                {
                    break; // EOF
                }
                string text = line.Trim();
                if (text.Length > 0)
                {
                    _queue.Enqueue(new Comment(Author, text));
                }
            }
        }, ct);
    }

    public IReadOnlyList<Comment> Drain(int max)
    {
        var picked = new List<Comment>();
        while (picked.Count < max && _queue.TryDequeue(out var comment))
        {
            picked.Add(comment);
        }
        return picked;
    }

    public void Dispose()
    {
        // Console.ReadLine でブロック中のワーカースレッドはプロセス終了時に回収される
        GC.SuppressFinalize(this);
    }
}
