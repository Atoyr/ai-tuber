using System.Text.Json;
using Medoz.Live;
using Medoz.Studio.Events;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// LiveEvent → SSE JSON 変換のテスト (docs/studio-architecture.md の SSE イベント形式)。
/// フロントエンド (wwwroot/app.js) はこの契約で実装されているため、event 名とキー名を厳守する。
/// </summary>
public class SseEventMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 7, 15, 20, 31, 5, TimeSpan.FromHours(9));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void CommentsPickedはcommentsイベントになる()
    {
        var comments = new[] { new Comment("viewer1", "初見です"), new Comment("viewer2", "こんばんは") };

        var sseEvent = SseEventMapper.Map(new CommentsPicked(At, comments));

        Assert.NotNull(sseEvent);
        Assert.Equal("comments", sseEvent!.Name);
        var data = Parse(sseEvent.Data);
        Assert.Contains("2026-07-15T20:31:05", data.GetProperty("at").GetString());
        var list = data.GetProperty("comments").EnumerateArray().ToArray();
        Assert.Equal(2, list.Length);
        Assert.Equal("viewer1", list[0].GetProperty("author").GetString());
        Assert.Equal("初見です", list[0].GetProperty("text").GetString());
    }

    [Fact]
    public void ReplySpokenはreplyイベントになる()
    {
        var sseEvent = SseEventMapper.Map(new ReplySpoken(At, "[joy]こんばんは!"));

        Assert.NotNull(sseEvent);
        Assert.Equal("reply", sseEvent!.Name);
        Assert.Equal("[joy]こんばんは!", Parse(sseEvent.Data).GetProperty("text").GetString());
    }

    [Fact]
    public void ReplySkippedはskipイベントになる()
    {
        var sseEvent = SseEventMapper.Map(new ReplySkipped(At, "フィルタに掛かった応答"));

        Assert.NotNull(sseEvent);
        Assert.Equal("skip", sseEvent!.Name);
        Assert.Equal("フィルタに掛かった応答", Parse(sseEvent.Data).GetProperty("reason").GetString());
    }

    [Fact]
    public void FreeTalkTriggeredはfreetalkイベントになる()
    {
        var sseEvent = SseEventMapper.Map(new FreeTalkTriggered(At));

        Assert.NotNull(sseEvent);
        Assert.Equal("freetalk", sseEvent!.Name);
        Assert.Contains("2026-07-15T20:31:05", Parse(sseEvent.Data).GetProperty("at").GetString());
    }

    [Fact]
    public void StreamNoteSavedはnoteイベントになる()
    {
        var sseEvent = SseEventMapper.Map(new StreamNoteSaved(At, "今日は初配信だった"));

        Assert.NotNull(sseEvent);
        Assert.Equal("note", sseEvent!.Name);
        Assert.Equal("今日は初配信だった", Parse(sseEvent.Data).GetProperty("summary").GetString());
    }

    [Fact]
    public void SessionStateChangedはnullを返しstateイベントは合成経由で流す()
    {
        // live 単独の状態変化はアプリ状態と合成した state イベントとして別経路で流す
        Assert.Null(SseEventMapper.Map(new SessionStateChanged(At, SessionState.Running)));
    }

    [Fact]
    public void Stateイベントは3アプリの状態を1つのJSONに合成する()
    {
        var sseEvent = SseEventMapper.State("Running", "Running", "RunningExternal");

        Assert.Equal("state", sseEvent.Name);
        var data = Parse(sseEvent.Data);
        Assert.Equal("Running", data.GetProperty("live").GetString());
        Assert.Equal("Running", data.GetProperty("voicevox").GetString());
        Assert.Equal("RunningExternal", data.GetProperty("purupuru").GetString());
    }

    [Fact]
    public void Logイベントはlevelとmessageを持つ()
    {
        var sseEvent = SseEventMapper.Log(At, "error", "起動に失敗しました");

        Assert.Equal("log", sseEvent.Name);
        var data = Parse(sseEvent.Data);
        Assert.Equal("error", data.GetProperty("level").GetString());
        Assert.Equal("起動に失敗しました", data.GetProperty("message").GetString());
    }
}
