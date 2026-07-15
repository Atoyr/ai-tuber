using Medoz.AiTuber.Core;

namespace Medoz.Studio.Settings;

/// <summary>
/// マージ後の実効設定 (エンジンデフォルト &lt; persona.json &lt; 環境変数 &lt; studio.json)。
/// セッション起動と GET /api/settings 応答の両方に使う。
/// </summary>
public sealed record EffectiveSettings
{
    // 即時反映
    public required double CommentBatchSec { get; init; }
    public required double FreetalkAfterSec { get; init; }
    public required bool FreetalkEnabled { get; init; }
    public required int SpeakerId { get; init; }
    public required bool Paused { get; init; }
    public required IReadOnlyDictionary<string, int> EmotionStyleIds { get; init; }

    // 次回セッションから
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required string OutputDevice { get; init; }
    public required string LlmProvider { get; init; }
    public required string LlmModel { get; init; }
    public required string PersonaDir { get; init; }
}

/// <summary>
/// AppConfig (エンジンデフォルト &lt; persona.json &lt; 環境変数 が既に反映済み) の上に
/// studio.json の「セット済み項目だけ」を上書きして実効設定を作る純粋ロジック。
/// 「UI で触った項目だけが env より優先」「触っていない項目は下位レイヤの値」をユニットテストする。
/// </summary>
public static class SettingsMerger
{
    public const string DefaultSource = "manual";

    public static EffectiveSettings Merge(AppConfig baseConfig, StudioSettingsData? overrides)
    {
        ArgumentNullException.ThrowIfNull(baseConfig);
        var o = overrides;

        return new EffectiveSettings
        {
            // AppConfig は秒を int で持つので double 化。studio.json があればそちらが勝つ。
            CommentBatchSec = o?.CommentBatchSec ?? baseConfig.CommentBatchSec,
            FreetalkAfterSec = o?.FreetalkAfterSec ?? baseConfig.FreetalkAfterSec,
            // FreetalkEnabled / Paused / Source / Target は Studio 固有 (AppConfig に無い) のでここが既定。
            FreetalkEnabled = o?.FreetalkEnabled ?? true,
            SpeakerId = o?.SpeakerId ?? baseConfig.SpeakerId,
            Paused = o?.Paused ?? false,
            EmotionStyleIds = baseConfig.EmotionStyleIds,
            Source = o?.Source ?? DefaultSource,
            Target = o?.Target ?? "",
            OutputDevice = o?.OutputDevice ?? baseConfig.OutputDeviceName,
            LlmProvider = o?.LlmProvider ?? baseConfig.LlmProvider,
            LlmModel = o?.LlmModel ?? baseConfig.LlmModel,
            PersonaDir = o?.PersonaDir ?? baseConfig.PersonaDir,
        };
    }
}
