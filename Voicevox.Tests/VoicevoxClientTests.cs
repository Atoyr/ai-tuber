using System.Net;
using System.Text;

namespace Medoz.Voicevox.Tests;

public class VoicevoxClientTests
{
    /// <summary>リクエストを記録し、URLに応じた固定レスポンスを返すハンドラ</summary>
    private class FakeHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();
        public string AudioQueryJson { get; init; } = """{"accent_phrases":[],"speedScale":1.0}""";
        public byte[] WavBytes { get; init; } = Encoding.ASCII.GetBytes("RIFFxxxxWAVE");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));

            if (request.RequestUri!.AbsolutePath == "/audio_query")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(AudioQueryJson, Encoding.UTF8, "application/json"),
                };
            }
            if (request.RequestUri.AbsolutePath == "/synthesis")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(WavBytes),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task SynthesizeAsync_AudioQueryとSynthesisを順に呼びWavを返す()
    {
        var handler = new FakeHandler();
        var client = new VoicevoxClient("http://127.0.0.1:50021", new HttpClient(handler));

        var wav = await client.SynthesizeAsync("こんにちは", speakerId: 3);

        Assert.Equal(handler.WavBytes, wav);
        Assert.Equal(2, handler.Requests.Count);

        var (queryRequest, _) = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, queryRequest.Method);
        Assert.Equal("/audio_query", queryRequest.RequestUri!.AbsolutePath);
        Assert.Contains($"text={Uri.EscapeDataString("こんにちは")}", queryRequest.RequestUri.Query);
        Assert.Contains("speaker=3", queryRequest.RequestUri.Query);

        var (synthesisRequest, synthesisBody) = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, synthesisRequest.Method);
        Assert.Equal("/synthesis", synthesisRequest.RequestUri!.AbsolutePath);
        Assert.Contains("speaker=3", synthesisRequest.RequestUri.Query);
        Assert.Equal(handler.AudioQueryJson, synthesisBody);
    }

    [Fact]
    public async Task SynthesizeAsync_APIエラー時は例外を投げる()
    {
        // FakeHandler は未知のパスに 404 を返すため、baseUrl に余計なパスを足して失敗させる
        var badClient = new VoicevoxClient("http://127.0.0.1:50021/unknown", new HttpClient(new FakeHandler()));
        await Assert.ThrowsAsync<HttpRequestException>(() => badClient.SynthesizeAsync("テスト"));
    }

    [Fact]
    public async Task SynthesizeAsync_空文字は引数例外()
    {
        var client = new VoicevoxClient(httpClient: new HttpClient(new FakeHandler()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SynthesizeAsync(""));
    }

    [Fact]
    public void Constructor_BaseUrl末尾のスラッシュは除去される()
    {
        var handler = new FakeHandler();
        var client = new VoicevoxClient("http://127.0.0.1:50021/", new HttpClient(handler));

        // URL が正しく組み立てられれば audio_query に到達する
        var wav = client.SynthesizeAsync("テスト").GetAwaiter().GetResult();
        Assert.NotEmpty(wav);
    }
}
