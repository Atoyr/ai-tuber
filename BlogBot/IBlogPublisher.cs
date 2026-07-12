using System.Diagnostics;

namespace Medoz.BlogBot;

/// <summary>公開先の抽象化。dry-run と実 publish (git push) を差し替えられるようにする。</summary>
public interface IBlogPublisher
{
    Task PublishAsync(BlogPost post, CancellationToken ct = default);
}

/// <summary>実 push せず、生成された markdown をコンソール表示するのみ (デフォルト挙動)</summary>
public sealed class DryRunBlogPublisher : IBlogPublisher
{
    public Task PublishAsync(BlogPost post, CancellationToken ct = default)
    {
        Console.WriteLine($"[DRY RUN] content/posts/{post.FileName}");
        Console.WriteLine("----------------------------------------");
        Console.WriteLine(BlogMessages.BuildMarkdown(post));
        Console.WriteLine("----------------------------------------");
        return Task.CompletedTask;
    }
}

/// <summary>
/// ai-tuber-blogs のローカルクローンに記事ファイルを書き込み、git commit → push する。
/// push の認証はユーザーの git 資格情報 (credential manager) に任せ、トークンをコードで扱わない。
/// </summary>
public sealed class GitBlogPublisher : IBlogPublisher
{
    private readonly string _repoPath;

    public GitBlogPublisher(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(Path.Combine(repoPath, ".git")))
        {
            throw new ArgumentException(
                $"BLOG_REPO_PATH が git リポジトリではありません: '{repoPath}' (ai-tuber-blogs をクローンしたパスを指定してください)",
                nameof(repoPath));
        }
        _repoPath = repoPath;
    }

    public async Task PublishAsync(BlogPost post, CancellationToken ct = default)
    {
        // リモートの先行変更 (手動記事追加など) と衝突しないよう、書き込む前に最新化する
        await RunGitAsync(ct, "pull", "--ff-only");

        string postsDir = Path.Combine(_repoPath, "content", "posts");
        Directory.CreateDirectory(postsDir);
        string path = ResolveUniquePath(postsDir, post.FileName);
        File.WriteAllText(path, BlogMessages.BuildMarkdown(post));

        string relative = Path.GetRelativePath(_repoPath, path).Replace('\\', '/');
        await RunGitAsync(ct, "add", relative);
        await RunGitAsync(ct, "commit", "-m", $"post: {post.Title}");
        await RunGitAsync(ct, "push");
        Console.WriteLine($"[公開完了] {relative}");
    }

    /// <summary>同名ファイルが既にある場合は上書きせず "-2", "-3" と連番を付ける</summary>
    public static string ResolveUniquePath(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            return path;
        }
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(dir, $"{stem}-{i}{ext}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private async Task RunGitAsync(CancellationToken ct, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git を起動できませんでした");
        string stdout = await process.StandardOutput.ReadToEndAsync(ct);
        string stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} が失敗しました (exit {process.ExitCode}):\n{stderr}\n{stdout}");
        }
    }
}
