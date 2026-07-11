namespace Medoz.GameCommentary;

/// <summary>
/// 実況テキストを発話する抽象。VOICEVOX 発話 (<see cref="VoicevoxSpeaker"/>) と、
/// コンソール出力のみの dry-run (<see cref="NullSpeaker"/>) を差し替えられるようにする。
/// </summary>
public interface ISpeaker
{
    Task SpeakAsync(string text, CancellationToken ct = default);
}

/// <summary>--no-voice 用。発話せず何もしない (コンソール出力はループ側が行う)。</summary>
public sealed class NullSpeaker : ISpeaker
{
    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
}
