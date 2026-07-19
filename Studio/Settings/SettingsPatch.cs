using System.Text.Json.Serialization;

namespace Medoz.Studio.Settings;

/// <summary>PUT /api/settings の受け取り (変更項目のみの部分オブジェクト)。</summary>
public sealed class SettingsPatch
{
    [JsonPropertyName("immediate")]
    public ImmediatePatch? Immediate { get; set; }

    [JsonPropertyName("nextSession")]
    public NextSessionPatch? NextSession { get; set; }
}

/// <summary>即時反映グループの部分更新。null のフィールドは未変更。</summary>
public sealed class ImmediatePatch
{
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

    [JsonPropertyName("captureIntervalSec")]
    public double? CaptureIntervalSec { get; set; }

    [JsonPropertyName("commentaryTimingMode")]
    public string? CommentaryTimingMode { get; set; }

    [JsonPropertyName("commentaryAfterSpeechSec")]
    public double? CommentaryAfterSpeechSec { get; set; }
}

/// <summary>次回セッショングループの部分更新。null のフィールドは未変更。</summary>
public sealed class NextSessionPatch
{
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
}
