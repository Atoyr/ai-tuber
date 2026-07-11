using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Medoz.Live;

/// <summary>
/// YouTube Live のコメントを取得する <see cref="ICommentSource"/> 実装。
/// YouTube Data API v3 を HttpClient で直接叩く (Google.Apis パッケージは使わない)。
///
/// 挙動は Python版 pytchat 利用 (comment_worker_youtube) に合わせ、
/// 「接続以降に投稿された新規コメントのみ」を流す (初回ページの既存コメントはスキップ)。
///
/// クォータ配慮のため liveChatMessages.list が返す pollingIntervalMillis に従って待機し、
/// nextPageToken を引き継いでポーリングする。
/// </summary>
public class YouTubeCommentSource : ICommentSource
{
    private const string ApiBase = "https://www.googleapis.com/youtube/v3/";

    /// <summary>pollingIntervalMillis が取得できない場合の待機時間 (ミリ秒)</summary>
    private const int DefaultPollIntervalMs = 5000;

    /// <summary>HTTP エラーなどで一時的に失敗したときの再試行待機時間 (ミリ秒)</summary>
    private const int RetryDelayMs = 5000;

    private readonly string _videoId;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    private readonly ConcurrentQueue<Comment> _queue = new();
    private Task? _worker;

    /// <summary>1ページ分の取得結果</summary>
    public record ChatPage(IReadOnlyList<Comment> Comments, string? NextPageToken, int PollingIntervalMillis);

    /// <param name="videoIdOrUrl">videoId または YouTube の視聴 URL</param>
    /// <param name="apiKey">YouTube Data API v3 の APIキー</param>
    /// <param name="handler">テスト用に注入する HttpMessageHandler (未指定なら既定の HttpClient を生成)</param>
    public YouTubeCommentSource(string videoIdOrUrl, string apiKey, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(videoIdOrUrl))
        {
            throw new ArgumentException("videoId または URL を指定してください。", nameof(videoIdOrUrl));
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("YouTube API キーを指定してください。", nameof(apiKey));
        }

        _videoId = ExtractVideoId(videoIdOrUrl);
        _apiKey = apiKey;
        // handler 注入時もラップした HttpClient を自前で破棄する
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
    }

    /// <summary>
    /// videoId、または YouTube の各種 URL 形式から videoId を抽出する。
    /// 対応: watch?v=XXXX / youtu.be/XXXX / live/XXXX / embed/XXXX / shorts/XXXX。
    /// URL でなければそのまま videoId とみなす。
    /// </summary>
    public static string ExtractVideoId(string input)
    {
        string value = input.Trim();

        if (!value.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            // URL でない → videoId そのものとして扱う
            return value;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            // youtu.be/XXXX
            if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
            {
                string path = uri.AbsolutePath.Trim('/');
                if (path.Length > 0)
                {
                    return path.Split('/')[0];
                }
            }

            // youtube.com/watch?v=XXXX
            string? v = GetQueryValue(uri.Query, "v");
            if (!string.IsNullOrEmpty(v))
            {
                return v;
            }

            // youtube.com/live/XXXX, /embed/XXXX, /shorts/XXXX
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2
                && (segments[0].Equals("live", StringComparison.OrdinalIgnoreCase)
                    || segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase)
                    || segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)))
            {
                return segments[1];
            }
        }

        // ここまでで抽出できなければ最後の手段として v= を正規表現で拾う
        var match = Regex.Match(value, @"[?&]v=([^&]+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        throw new ArgumentException($"URL から videoId を抽出できませんでした: {input}", nameof(input));
    }

    /// <summary>クエリ文字列 (先頭 '?' 有無どちらでも可) から指定キーの値を取り出す</summary>
    private static string? GetQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            if (pair[..eq].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }
        return null;
    }

    public void Start(CancellationToken ct)
    {
        // 起動時に activeLiveChatId を解決する。ライブでなければここで例外を投げて即座に気付けるようにする。
        string liveChatId = ResolveActiveLiveChatIdAsync(ct).GetAwaiter().GetResult();
        Console.WriteLine($"YouTube Live に接続しました (videoId={_videoId})");

        _worker = Task.Run(() => PollLoopAsync(liveChatId, ct), ct);
    }

    /// <summary>
    /// videos.list で対象動画の activeLiveChatId を取得する。
    /// 動画が存在しない / ライブ配信でない / チャットが無効な場合は分かりやすい例外を投げる。
    /// </summary>
    public async Task<string> ResolveActiveLiveChatIdAsync(CancellationToken ct)
    {
        string url = $"{ApiBase}videos?part=liveStreamingDetails&id={Uri.EscapeDataString(_videoId)}&key={Uri.EscapeDataString(_apiKey)}";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"動画が見つかりません (videoId={_videoId})。videoId を確認してください。");
        }

        var item = items[0];
        if (!item.TryGetProperty("liveStreamingDetails", out var details))
        {
            throw new InvalidOperationException($"この動画はライブ配信ではありません (videoId={_videoId})。");
        }

        if (!details.TryGetProperty("activeLiveChatId", out var chatId) || chatId.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"アクティブなライブチャットがありません (videoId={_videoId})。配信が開始済みでチャットが有効か確認してください。");
        }

        return chatId.GetString()!;
    }

    /// <summary>
    /// liveChatMessages.list を 1 回叩き、コメント一覧・nextPageToken・pollingIntervalMillis を返す。
    /// </summary>
    public async Task<ChatPage> FetchMessagesAsync(string liveChatId, string? pageToken, CancellationToken ct)
    {
        string url = $"{ApiBase}liveChatMessages?liveChatId={Uri.EscapeDataString(liveChatId)}"
                   + $"&part=snippet,authorDetails&key={Uri.EscapeDataString(_apiKey)}";
        if (!string.IsNullOrEmpty(pageToken))
        {
            url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
        }

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var comments = new List<Comment>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                string author = item.TryGetProperty("authorDetails", out var ad)
                    && ad.TryGetProperty("displayName", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString()!
                    : "名無し";

                string? message = null;
                if (item.TryGetProperty("snippet", out var snippet)
                    && snippet.TryGetProperty("displayMessage", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    message = msg.GetString();
                }

                // テキストコメント以外 (スーパーチャットの金額のみ等) は displayMessage が無いのでスキップ
                if (!string.IsNullOrEmpty(message))
                {
                    comments.Add(new Comment(author, message));
                }
            }
        }

        string? nextPageToken = root.TryGetProperty("nextPageToken", out var tok) && tok.ValueKind == JsonValueKind.String
            ? tok.GetString()
            : null;

        int pollingMs = root.TryGetProperty("pollingIntervalMillis", out var poll)
            && poll.TryGetInt32(out int ms)
            ? ms
            : DefaultPollIntervalMs;

        return new ChatPage(comments, nextPageToken, pollingMs);
    }

    private async Task PollLoopAsync(string liveChatId, CancellationToken ct)
    {
        string? pageToken = null;
        bool firstPage = true; // 接続以降の新規コメントのみを流す (初回ページの既存分は捨てる)

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var page = await FetchMessagesAsync(liveChatId, pageToken, ct);

                if (!firstPage)
                {
                    foreach (var comment in page.Comments)
                    {
                        _queue.Enqueue(comment);
                    }
                }
                firstPage = false;
                pageToken = page.NextPageToken;

                int delayMs = page.PollingIntervalMillis > 0 ? page.PollingIntervalMillis : DefaultPollIntervalMs;
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 一時的な HTTP エラー等ではループを殺さず、ログを出して再試行する
                Console.WriteLine($"[youtube] コメント取得エラー: {ex.Message} ({RetryDelayMs / 1000}秒後に再試行)");
                try
                {
                    await Task.Delay(RetryDelayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
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
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
