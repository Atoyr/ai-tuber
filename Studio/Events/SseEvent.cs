using System.Text.Json;
using Medoz.Live;

namespace Medoz.Studio.Events;

/// <summary>SSE で配信する1イベント (event 名 + JSON data)。</summary>
public sealed record SseEvent(string Name, string Data)
{
    /// <summary>SSE ワイヤ形式 (<c>event: ...\ndata: ...\n\n</c>) にする。data 内の改行も規約どおり分割する。</summary>
    public string ToWireFormat()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("event: ").Append(Name).Append('\n');
        foreach (string line in Data.Split('\n'))
        {
            sb.Append("data: ").Append(line).Append('\n');
        }
        sb.Append('\n');
        return sb.ToString();
    }
}

/// <summary>
/// <see cref="LiveEvent"/> → SSE イベントへの変換 (docs/studio-architecture.md の契約)。
/// pure static にしてユニットテスト対象にする。JSON はキャメルケース。
/// </summary>
public static class SseEventMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// LiveEvent を SSE イベントに変換する。<see cref="SessionStateChanged"/> は
    /// state イベント (アプリ状態と合成) として別経路で流すため null を返す。
    /// </summary>
    public static SseEvent? Map(LiveEvent liveEvent) => liveEvent switch
    {
        CommentsPicked picked => new SseEvent("comments", JsonSerializer.Serialize(new
        {
            at = picked.At,
            comments = picked.Comments.Select(c => new { author = c.Author, text = c.Message }).ToArray(),
        }, JsonOptions)),
        ReplySpoken spoken => new SseEvent("reply", JsonSerializer.Serialize(new
        {
            at = spoken.At,
            text = spoken.Text,
        }, JsonOptions)),
        ReplySkipped skipped => new SseEvent("skip", JsonSerializer.Serialize(new
        {
            at = skipped.At,
            reason = skipped.Reason,
        }, JsonOptions)),
        FreeTalkTriggered freetalk => new SseEvent("freetalk", JsonSerializer.Serialize(new
        {
            at = freetalk.At,
        }, JsonOptions)),
        StreamNoteSaved note => new SseEvent("note", JsonSerializer.Serialize(new
        {
            at = note.At,
            summary = note.Summary,
        }, JsonOptions)),
        SessionStateChanged => null, // state イベントとして別経路 (アプリ状態と合成) で流す
        _ => null,
    };

    /// <summary>live / voicevox / purupuru / commentary の状態を合成した state イベントを作る。</summary>
    public static SseEvent State(string live, string voicevox, string purupuru, string commentary)
        => new("state", JsonSerializer.Serialize(new { live, voicevox, purupuru, commentary }, JsonOptions));

    /// <summary>ゲーム実況の発話 (commentary イベント) を作る。</summary>
    public static SseEvent Commentary(DateTimeOffset at, string text)
        => new("commentary", JsonSerializer.Serialize(new { at, text }, JsonOptions));

    /// <summary>システムメッセージの log イベントを作る。</summary>
    public static SseEvent Log(DateTimeOffset at, string level, string message)
        => new("log", JsonSerializer.Serialize(new { at, level, message }, JsonOptions));
}
