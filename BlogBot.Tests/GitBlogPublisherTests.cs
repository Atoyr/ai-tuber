namespace Medoz.BlogBot.Tests;

public class GitBlogPublisherTests : IDisposable
{
    private readonly string _dir;

    public GitBlogPublisherTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "blogbot-posts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveUniquePath_ReturnsOriginal_WhenNotExists()
    {
        string path = GitBlogPublisher.ResolveUniquePath(_dir, "2026-07-08-post.md");

        Assert.Equal(Path.Combine(_dir, "2026-07-08-post.md"), path);
    }

    [Fact]
    public void ResolveUniquePath_AppendsSequence_WhenExists()
    {
        File.WriteAllText(Path.Combine(_dir, "2026-07-08-post.md"), "");
        File.WriteAllText(Path.Combine(_dir, "2026-07-08-post-2.md"), "");

        string path = GitBlogPublisher.ResolveUniquePath(_dir, "2026-07-08-post.md");

        Assert.Equal(Path.Combine(_dir, "2026-07-08-post-3.md"), path);
    }

    [Fact]
    public void Constructor_Throws_WhenNotGitRepo()
    {
        Assert.Throws<ArgumentException>(() => new GitBlogPublisher(_dir));
    }
}
