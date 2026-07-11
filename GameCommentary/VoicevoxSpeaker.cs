using Medoz.Voicevox;

namespace Medoz.GameCommentary;

/// <summary>
/// VOICEVOX で音声合成し、指定の出力デバイス(VB-CABLE等)へ再生する発話実装。
/// Python版 live_aituber.speak を game_commentary から呼んでいたのに相当する。
/// </summary>
public sealed class VoicevoxSpeaker : ISpeaker, IDisposable
{
    private readonly VoicevoxClient _voicevox;
    private readonly AudioPlayer _player;
    private readonly int _speakerId;
    private readonly EmotionStyleMap? _styleMap;

    /// <param name="styleMap">
    /// Phase H の感情タグ (character.md) を解釈するマップ。指定すると発話先頭の
    /// <c>[joy]</c> 等を本文から除去し、対応する VOICEVOX スタイルに切り替える。
    /// null なら従来どおり speakerId 固定・タグ処理なし。
    /// </param>
    public VoicevoxSpeaker(VoicevoxClient voicevox, AudioPlayer player, int speakerId,
                           EmotionStyleMap? styleMap = null)
    {
        _voicevox = voicevox ?? throw new ArgumentNullException(nameof(voicevox));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _speakerId = speakerId;
        _styleMap = styleMap;
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        int styleId = _speakerId;
        string body = text;
        if (_styleMap is not null)
        {
            EmotionParseResult parsed = EmotionTagParser.Parse(text);
            body = parsed.Text.Length > 0 ? parsed.Text : text;
            styleId = _styleMap.Resolve(parsed.Emotion);
        }
        byte[] wav = await _voicevox.SynthesizeAsync(body, styleId, ct);
        await _player.PlayWavAsync(wav, ct);
    }

    public void Dispose() => _player.Dispose();
}
