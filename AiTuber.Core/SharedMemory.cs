using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Medoz.AiTuber.Core;

/// <summary>配信メモ1件。JSONキーは Python版 memory.json と互換</summary>
public record StreamNote(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("summary")] string Summary);

/// <summary>公開したブログ記事1件の記録 (重複回避用)</summary>
public record PostNote(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("title")] string Title);

/// <summary>data/memory.json の中身</summary>
public class MemoryData
{
    [JsonPropertyName("stream_notes")]
    public List<StreamNote> StreamNotes { get; set; } = new();

    /// <summary>直近ツイート本文 (新しい順)</summary>
    [JsonPropertyName("recent_tweets")]
    public List<string> RecentTweets { get; set; } = new();

    /// <summary>直近に公開したブログ記事 (新しい順)</summary>
    [JsonPropertyName("recent_posts")]
    public List<PostNote> RecentPosts { get; set; } = new();
}

/// <summary>
/// 配信と Twitter で共有する簡易メモリ (Python版 core/memory.py 相当)。
/// - 配信側が「今日何を配信したか」を書き込む
/// - Twitterボットがそれを読んで「配信お礼/次回予告」ツイートに使う
/// - Twitterボットは投稿履歴を書き込み、重複ツイートを防ぐ
/// </summary>
public class SharedMemory
{
    private const int StreamNotesKeep = 10;
    private const int RecentPostsKeep = 10;
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        // 日本語をエスケープせずそのまま出力する (Python版 ensure_ascii=False 相当)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly int _recentTweetsKeep;

    public SharedMemory(string path, int recentTweetsKeep = 20)
    {
        _path = path;
        _recentTweetsKeep = recentTweetsKeep;
    }

    public MemoryData Load()
    {
        if (!File.Exists(_path))
        {
            return new MemoryData();
        }
        return JsonSerializer.Deserialize<MemoryData>(File.ReadAllText(_path)) ?? new MemoryData();
    }

    public void Save(MemoryData memory)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(_path, JsonSerializer.Serialize(memory, _jsonOptions));
    }

    /// <summary>配信メモを追記する。最大10件を保持</summary>
    public void AddStreamNote(string summary) => AddStreamNote(summary, DateTime.Now);

    public void AddStreamNote(string summary, DateTime now)
    {
        var memory = Load();
        memory.StreamNotes.Add(new StreamNote(now.ToString(DateFormat, CultureInfo.InvariantCulture), summary));
        if (memory.StreamNotes.Count > StreamNotesKeep)
        {
            memory.StreamNotes.RemoveRange(0, memory.StreamNotes.Count - StreamNotesKeep);
        }
        Save(memory);
    }

    /// <summary>公開したブログ記事を先頭に記録する。最大10件を保持</summary>
    public void AddPost(string title) => AddPost(title, DateTime.Now);

    public void AddPost(string title, DateTime now)
    {
        var memory = Load();
        memory.RecentPosts.Insert(0, new PostNote(now.ToString(DateFormat, CultureInfo.InvariantCulture), title));
        if (memory.RecentPosts.Count > RecentPostsKeep)
        {
            memory.RecentPosts.RemoveRange(RecentPostsKeep, memory.RecentPosts.Count - RecentPostsKeep);
        }
        Save(memory);
    }

    /// <summary>投稿したツイートを先頭に記録する。最大 recentTweetsKeep 件を保持</summary>
    public void AddTweet(string text)
    {
        var memory = Load();
        memory.RecentTweets.Insert(0, text);
        if (memory.RecentTweets.Count > _recentTweetsKeep)
        {
            memory.RecentTweets.RemoveRange(_recentTweetsKeep, memory.RecentTweets.Count - _recentTweetsKeep);
        }
        Save(memory);
    }
}
