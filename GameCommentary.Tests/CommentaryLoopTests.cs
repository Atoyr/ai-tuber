using Medoz.AiTuber.Core;
using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

public class CommentaryLoopTests : IDisposable
{
    private readonly string _promptDir;
    private static readonly string[] BannedWords = { "死ね", "殺す", "http://", "@" };

    public CommentaryLoopTests()
    {
        _promptDir = Path.Combine(Path.GetTempPath(), "gamecommentary-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_promptDir);
        File.WriteAllText(Path.Combine(_promptDir, "persona.json"),
            """{ "schemaVersion": 1, "name": "ぽとふ", "slug": "potofu", "voice": { "speakerId": 3, "emotionStyles": {} } }""");
        File.WriteAllText(Path.Combine(_promptDir, "character.md"), "あなたはぽとふです。");
        File.WriteAllText(Path.Combine(_promptDir, "game_system.md"), "実況モードの指示。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_promptDir))
        {
            Directory.Delete(_promptDir, recursive: true);
        }
    }

    private CommentaryLoop CreateLoop(FakeChatClient client, FakeWindowCapture capture,
                                      FakeSpeaker speaker, int historyLimit = 4,
                                      Action<string>? log = null, int maxTokens = 500)
    {
        var persona = new Persona(client, PersonaPackage.Load(_promptDir), "game_system.md");
        var filter = new ModerationFilter(BannedWords);
        return new CommentaryLoop(capture, persona, filter, speaker, historyLimit, "テスト", log, maxTokens);
    }

    [Fact]
    public async Task RunOnceAsync_Success_SpeaksAndRecordsHistory()
    {
        var client = new FakeChatClient("敵が出てきたよ!");
        var capture = new FakeWindowCapture();
        var speaker = new FakeSpeaker();
        var loop = CreateLoop(client, capture, speaker);

        string? result = await loop.RunOnceAsync();

        Assert.Equal("敵が出てきたよ!", result);
        Assert.Equal(new[] { "敵が出てきたよ!" }, loop.History);
        Assert.Equal(new[] { "敵が出てきたよ!" }, speaker.Spoken);
        // 画像が base64 化されて渡っていること
        Assert.Single(client.ReceivedImages);
        Assert.Equal("image/jpeg", client.ReceivedImages[0].MediaType);
        Assert.False(string.IsNullOrEmpty(client.ReceivedImages[0].Base64Data));
    }

    [Fact]
    public async Task RunOnceAsync_FilterViolation_SkipsWithoutPollutingHistory()
    {
        // "@" は禁止ワード
        var client = new FakeChatClient("@メンションだよ");
        var capture = new FakeWindowCapture();
        var speaker = new FakeSpeaker();
        var loop = CreateLoop(client, capture, speaker);

        string? result = await loop.RunOnceAsync();

        Assert.Null(result);
        Assert.Empty(loop.History);
        Assert.Empty(speaker.Spoken);
    }

    [Fact]
    public async Task RunOnceAsync_CaptureError_SkipsWithoutPollutingHistory()
    {
        var client = new FakeChatClient("使われないはず");
        var capture = new FakeWindowCapture(toThrow: new InvalidOperationException("キャプチャ失敗"));
        var speaker = new FakeSpeaker();
        var loop = CreateLoop(client, capture, speaker);

        // 例外を投げずに null を返し、ループを殺さないこと
        string? result = await loop.RunOnceAsync();

        Assert.Null(result);
        Assert.Empty(loop.History);
        Assert.Empty(speaker.Spoken);
        Assert.Equal(0, client.CallCount); // 生成まで到達しない
    }

    [Fact]
    public async Task RunOnceAsync_ReportsSkipAndErrorToInjectedLog()
    {
        // Studio が SSE へ流すための注入ログに [skip] / [error] が届くこと
        var logs = new List<string>();
        var speaker = new FakeSpeaker();

        var filtered = CreateLoop(new FakeChatClient("@メンションだよ"), new FakeWindowCapture(), speaker, log: logs.Add);
        await filtered.RunOnceAsync();

        var captureFailed = CreateLoop(new FakeChatClient("使われないはず"),
            new FakeWindowCapture(toThrow: new InvalidOperationException("キャプチャ失敗")), speaker, log: logs.Add);
        await captureFailed.RunOnceAsync();

        Assert.Contains(logs, m => m.StartsWith("[skip]") && m.Contains("@メンションだよ"));
        Assert.Contains(logs, m => m.StartsWith("[error]") && m.Contains("キャプチャ失敗"));
    }

    [Fact]
    public async Task RunOnceAsync_KeepsHistoryToLimit()
    {
        var client = new FakeChatClient("A", "B", "C", "D", "E");
        var capture = new FakeWindowCapture();
        var speaker = new FakeSpeaker();
        var loop = CreateLoop(client, capture, speaker, historyLimit: 4);

        for (int i = 0; i < 5; i++)
        {
            await loop.RunOnceAsync();
        }

        // 直近4件のみ保持
        Assert.Equal(new[] { "B", "C", "D", "E" }, loop.History);
    }

    [Fact]
    public async Task RunOnceAsync_PassesDefaultMaxTokensToClient()
    {
        // 実況が単語で途切れないよう、既定の maxTokens は 500 (COMMENTARY_MAX_TOKENS で変更可)
        var client = new FakeChatClient("長い実況");
        var loop = new CommentaryLoop(new FakeWindowCapture(),
            new Persona(client, PersonaPackage.Load(_promptDir), "game_system.md"),
            new ModerationFilter(BannedWords), new FakeSpeaker());

        await loop.RunOnceAsync();

        Assert.Equal(new[] { 500 }, client.ReceivedMaxTokens);
    }

    [Fact]
    public async Task RunOnceAsync_PassesConfiguredMaxTokensToClient()
    {
        var client = new FakeChatClient("長い実況");
        var loop = CreateLoop(client, new FakeWindowCapture(), new FakeSpeaker(), maxTokens: 800);

        await loop.RunOnceAsync();

        Assert.Equal(new[] { 800 }, client.ReceivedMaxTokens);
    }

    [Fact]
    public async Task RunOnceAsync_PassesRecentHistoryAsContext()
    {
        var client = new FakeChatClient("一言目", "二言目");
        var capture = new FakeWindowCapture();
        var speaker = new FakeSpeaker();
        var loop = CreateLoop(client, capture, speaker);

        await loop.RunOnceAsync(); // 1回目: 履歴なし
        await loop.RunOnceAsync(); // 2回目: 直近実況に "一言目" が入る

        Assert.Equal(CommentaryMessages.CommentaryRequest, client.ReceivedTexts[0]);
        Assert.Contains("直近の実況: 一言目", client.ReceivedTexts[1]);
    }
}
