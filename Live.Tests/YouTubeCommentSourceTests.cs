using System.Net;
using System.Text;
using Medoz.Live;

namespace Medoz.Live.Tests;

public class YouTubeCommentSourceTests
{
    // ---- テスト用の HttpMessageHandler ----
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestedUrls { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            return Task.FromResult(_responder(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---- videoId 抽出 ----
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123XYZ", "abc123XYZ")]
    [InlineData("https://www.youtube.com/watch?v=abc123XYZ&t=42s", "abc123XYZ")]
    [InlineData("https://youtu.be/abc123XYZ", "abc123XYZ")]
    [InlineData("https://youtu.be/abc123XYZ?si=xxxx", "abc123XYZ")]
    [InlineData("https://www.youtube.com/live/abc123XYZ", "abc123XYZ")]
    [InlineData("https://www.youtube.com/embed/abc123XYZ", "abc123XYZ")]
    [InlineData("https://www.youtube.com/shorts/abc123XYZ", "abc123XYZ")]
    [InlineData("abc123XYZ", "abc123XYZ")]
    public void ExtractVideoId_ParsesVariousForms(string input, string expected)
    {
        Assert.Equal(expected, YouTubeCommentSource.ExtractVideoId(input));
    }

    [Fact]
    public async Task ResolveActiveLiveChatId_ReturnsId_WhenLive()
    {
        var handler = new FakeHandler(_ => Json("""
            { "items": [ { "liveStreamingDetails": { "activeLiveChatId": "CHAT_ID_123" } } ] }
            """));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        string chatId = await source.ResolveActiveLiveChatIdAsync(CancellationToken.None);

        Assert.Equal("CHAT_ID_123", chatId);
        Assert.Contains("videos?part=liveStreamingDetails", handler.RequestedUrls[0]);
        Assert.Contains("id=VIDEO", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task ResolveActiveLiveChatId_Throws_WhenVideoNotFound()
    {
        var handler = new FakeHandler(_ => Json("""{ "items": [] }"""));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveActiveLiveChatIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveActiveLiveChatId_Throws_WhenNotLive()
    {
        // ライブ配信でない (liveStreamingDetails が無い)
        var handler = new FakeHandler(_ => Json("""{ "items": [ { } ] }"""));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveActiveLiveChatIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ResolveActiveLiveChatId_Throws_WhenNoActiveChat()
    {
        // 配信枠はあるがチャットが有効でない (activeLiveChatId が無い)
        var handler = new FakeHandler(_ => Json("""
            { "items": [ { "liveStreamingDetails": { "scheduledStartTime": "2026-01-01T00:00:00Z" } } ] }
            """));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveActiveLiveChatIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FetchMessages_ParsesAuthorAndMessage()
    {
        var handler = new FakeHandler(_ => Json("""
            {
              "pollingIntervalMillis": 2500,
              "nextPageToken": "NEXT_TOKEN",
              "items": [
                { "snippet": { "displayMessage": "こんにちは!" }, "authorDetails": { "displayName": "太郎" } },
                { "snippet": { "displayMessage": "初見です" },   "authorDetails": { "displayName": "花子" } }
              ]
            }
            """));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        var page = await source.FetchMessagesAsync("CHAT_ID", pageToken: null, CancellationToken.None);

        Assert.Equal(2, page.Comments.Count);
        Assert.Equal("太郎", page.Comments[0].Author);
        Assert.Equal("こんにちは!", page.Comments[0].Message);
        Assert.Equal("花子", page.Comments[1].Author);
        Assert.Equal("初見です", page.Comments[1].Message);
        Assert.Equal(2500, page.PollingIntervalMillis);
        Assert.Equal("NEXT_TOKEN", page.NextPageToken);
    }

    [Fact]
    public async Task FetchMessages_SkipsItemsWithoutDisplayMessage()
    {
        // displayMessage の無いイベント (スーパーチャットの金額のみ等) はスキップされる
        var handler = new FakeHandler(_ => Json("""
            {
              "pollingIntervalMillis": 1000,
              "items": [
                { "snippet": { }, "authorDetails": { "displayName": "無言さん" } },
                { "snippet": { "displayMessage": "やあ" }, "authorDetails": { "displayName": "太郎" } }
              ]
            }
            """));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        var page = await source.FetchMessagesAsync("CHAT_ID", pageToken: null, CancellationToken.None);

        Assert.Single(page.Comments);
        Assert.Equal("太郎", page.Comments[0].Author);
    }

    [Fact]
    public async Task FetchMessages_CarriesPageTokenIntoRequest()
    {
        var handler = new FakeHandler(_ => Json("""{ "pollingIntervalMillis": 1000, "items": [] }"""));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        await source.FetchMessagesAsync("CHAT_ID", pageToken: "TOKEN_ABC", CancellationToken.None);

        Assert.Contains("liveChatId=CHAT_ID", handler.RequestedUrls[0]);
        Assert.Contains("pageToken=TOKEN_ABC", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task FetchMessages_DefaultsPolling_WhenMissing()
    {
        var handler = new FakeHandler(_ => Json("""{ "items": [] }"""));
        using var source = new YouTubeCommentSource("VIDEO", "KEY", handler);

        var page = await source.FetchMessagesAsync("CHAT_ID", pageToken: null, CancellationToken.None);

        Assert.True(page.PollingIntervalMillis > 0); // 既定値にフォールバック
        Assert.Null(page.NextPageToken);
        Assert.Empty(page.Comments);
    }
}
