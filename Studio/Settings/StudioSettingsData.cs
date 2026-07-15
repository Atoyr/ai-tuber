using System.Text.Json.Serialization;

namespace Medoz.Studio.Settings;

/// <summary>
/// data/studio.json に保存する「UI で明示的に変更した項目だけ」(docs/studio-architecture.md)。
/// null = 未変更 = 下位レイヤ (エンジンデフォルト &lt; persona.json &lt; 環境変数) の値が生きる。
/// 全設定のスナップショットにはしない (env や persona を変えたのに古い値が勝ち続ける事故を防ぐ)。
/// </summary>
public sealed class StudioSettingsData
{
    // --- 即時反映 (実行中セッションへ反映) ---
    [JsonPropertyName("commentBatchSec")]
    public double? CommentBatchSec { get; set; }

    [JsonPropertyName("freetalkAfterSec")]
    public double? FreetalkAfterSec { get; set; }

    [JsonPropertyName("freetalkEnabled")]
    public bool? FreetalkEnabled { get; set; }

    [JsonPropertyName("speakerId")]
    public int? SpeakerId { get; set; }

    [JsonPropertyName("paused")]
    public bool? Paused { get; set; }

    // --- 次回セッションから反映 ---
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("outputDevice")]
    public string? OutputDevice { get; set; }

    [JsonPropertyName("llmProvider")]
    public string? LlmProvider { get; set; }

    [JsonPropertyName("llmModel")]
    public string? LlmModel { get; set; }

    [JsonPropertyName("personaDir")]
    public string? PersonaDir { get; set; }

    public StudioSettingsData Clone() => new()
    {
        CommentBatchSec = CommentBatchSec,
        FreetalkAfterSec = FreetalkAfterSec,
        FreetalkEnabled = FreetalkEnabled,
        SpeakerId = SpeakerId,
        Paused = Paused,
        Source = Source,
        Target = Target,
        OutputDevice = OutputDevice,
        LlmProvider = LlmProvider,
        LlmModel = LlmModel,
        PersonaDir = PersonaDir,
    };
}
