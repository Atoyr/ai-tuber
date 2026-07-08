using Medoz.AiTuber.Core;

namespace Medoz.AiTuber.Core.Tests;

public class SharedMemoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _memoryPath;

    public SharedMemoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "aituber-tests-" + Guid.NewGuid().ToString("N"));
        _memoryPath = Path.Combine(_tempDir, "data", "memory.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsEmptyDefault_WhenFileDoesNotExist()
    {
        var memory = new SharedMemory(_memoryPath);

        var data = memory.Load();

        Assert.Empty(data.StreamNotes);
        Assert.Empty(data.RecentTweets);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var memory = new SharedMemory(_memoryPath);
        var data = new MemoryData
        {
            StreamNotes = { new StreamNote("2026-07-09 12:00", "ゲーム配信をした") },
            RecentTweets = { "こんにちは!" },
        };

        memory.Save(data);
        var loaded = memory.Load();

        Assert.Single(loaded.StreamNotes);
        Assert.Equal("2026-07-09 12:00", loaded.StreamNotes[0].Date);
        Assert.Equal("ゲーム配信をした", loaded.StreamNotes[0].Summary);
        Assert.Equal(new[] { "こんにちは!" }, loaded.RecentTweets);
    }

    [Fact]
    public void Save_WritesPythonCompatibleJsonKeys_AndUnescapedJapanese()
    {
        var memory = new SharedMemory(_memoryPath);
        memory.Save(new MemoryData
        {
            StreamNotes = { new StreamNote("2026-07-09 12:00", "要約") },
            RecentTweets = { "ツイート" },
        });

        string json = File.ReadAllText(_memoryPath);
        Assert.Contains("\"stream_notes\"", json);
        Assert.Contains("\"recent_tweets\"", json);
        Assert.Contains("\"date\"", json);
        Assert.Contains("\"summary\"", json);
        // ensure_ascii=False 相当: 日本語が \uXXXX にエスケープされない
        Assert.Contains("要約", json);
    }

    [Fact]
    public void AddStreamNote_AppendsWithFormattedDate_AndKeepsLast10()
    {
        var memory = new SharedMemory(_memoryPath);
        for (int i = 1; i <= 12; i++)
        {
            memory.AddStreamNote($"note {i}", new DateTime(2026, 7, 9, 12, 0, 0).AddMinutes(i));
        }

        var data = memory.Load();

        Assert.Equal(10, data.StreamNotes.Count);
        // 古い2件 (note 1, note 2) が捨てられる
        Assert.Equal("note 3", data.StreamNotes[0].Summary);
        Assert.Equal("note 12", data.StreamNotes[^1].Summary);
        Assert.Equal("2026-07-09 12:03", data.StreamNotes[0].Date);
    }

    [Fact]
    public void AddTweet_InsertsAtHead_AndKeepsConfiguredCount()
    {
        var memory = new SharedMemory(_memoryPath, recentTweetsKeep: 3);
        memory.AddTweet("tweet 1");
        memory.AddTweet("tweet 2");
        memory.AddTweet("tweet 3");
        memory.AddTweet("tweet 4");

        var data = memory.Load();

        // 新しい順に最大3件
        Assert.Equal(new[] { "tweet 4", "tweet 3", "tweet 2" }, data.RecentTweets);
    }

    [Fact]
    public void Save_CreatesParentDirectories()
    {
        var memory = new SharedMemory(_memoryPath);

        memory.Save(new MemoryData());

        Assert.True(File.Exists(_memoryPath));
    }
}
