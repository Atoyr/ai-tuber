using Medoz.AiTuber.Core;
using Medoz.Studio.Settings;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// studio.json マージのテスト (docs/studio-architecture.md)。
/// 優先順位: エンジンデフォルト &lt; persona.json &lt; 環境変数 &lt; studio.json (UI で触った項目のみ)。
/// AppConfig が「デフォルト &lt; persona &lt; env」を担い、SettingsMerger はその上に studio.json を重ねる。
/// </summary>
public class SettingsMergerTests
{
    [Fact]
    public void studioJsonが無ければ下位レイヤの値がそのまま生きる()
    {
        var config = new AppConfig
        {
            CommentBatchSec = 4,
            FreetalkAfterSec = 45,
            SpeakerId = 3,
            OutputDeviceName = "CABLE Input",
            LlmProvider = "claude",
            PersonaDir = "personas/default",
        };

        var effective = SettingsMerger.Merge(config, overrides: null);

        Assert.Equal(4, effective.CommentBatchSec);
        Assert.Equal(45, effective.FreetalkAfterSec);
        Assert.True(effective.FreetalkEnabled);
        Assert.Equal(3, effective.SpeakerId);
        Assert.False(effective.Paused);
        Assert.Equal(12, effective.CaptureIntervalSec);       // AppConfig の既定 (CAPTURE_INTERVAL_SEC)
        Assert.Equal("interval", effective.CommentaryTimingMode); // Studio 固有: 既定は従来挙動
        Assert.Equal(5, effective.CommentaryAfterSpeechSec);
        Assert.Equal("manual", effective.Source);
        Assert.Equal("CABLE Input", effective.OutputDevice);
        Assert.Equal("claude", effective.LlmProvider);
        Assert.Equal("claude-sonnet-4-6", effective.LlmModel);
        Assert.Equal("personas/default", effective.PersonaDir);
    }

    [Fact]
    public void UIで触った項目だけがenvより優先される()
    {
        // env 由来 (AppConfig にロード済み) の値
        var config = new AppConfig
        {
            SpeakerId = 3,
            OutputDeviceName = "CABLE Input",
        };
        // UI では speakerId だけ変更した
        var studio = new StudioSettingsData { SpeakerId = 8 };

        var effective = SettingsMerger.Merge(config, studio);

        Assert.Equal(8, effective.SpeakerId);              // 触った項目: studio.json が勝つ
        Assert.Equal("CABLE Input", effective.OutputDevice); // 触っていない項目: env/デフォルトが生きる
        Assert.Equal(4, effective.CommentBatchSec);
    }

    [Fact]
    public void 全項目を上書きできる()
    {
        var config = new AppConfig();
        var studio = new StudioSettingsData
        {
            CommentBatchSec = 2.5,
            FreetalkAfterSec = 90,
            FreetalkEnabled = false,
            SpeakerId = 1,
            Paused = true,
            CaptureIntervalSec = 30,
            CommentaryTimingMode = "afterSpeech",
            CommentaryAfterSpeechSec = 8,
            Source = "twitch",
            Target = "somechannel",
            OutputDevice = "VoiceMeeter",
            LlmProvider = "gemini",
            LlmModel = "gemini-x",
            PersonaDir = "../ai-tuber-potofu",
        };

        var effective = SettingsMerger.Merge(config, studio);

        Assert.Equal(2.5, effective.CommentBatchSec);
        Assert.Equal(90, effective.FreetalkAfterSec);
        Assert.False(effective.FreetalkEnabled);
        Assert.Equal(1, effective.SpeakerId);
        Assert.True(effective.Paused);
        Assert.Equal(30, effective.CaptureIntervalSec);
        Assert.Equal("afterSpeech", effective.CommentaryTimingMode);
        Assert.Equal(8, effective.CommentaryAfterSpeechSec);
        Assert.Equal("twitch", effective.Source);
        Assert.Equal("somechannel", effective.Target);
        Assert.Equal("VoiceMeeter", effective.OutputDevice);
        Assert.Equal("gemini", effective.LlmProvider);
        Assert.Equal("gemini-x", effective.LlmModel);
        Assert.Equal("../ai-tuber-potofu", effective.PersonaDir);
    }

    [Fact]
    public void LlmModelを触っていなければプロバイダに応じた既定モデルが返る()
    {
        var config = new AppConfig { LlmProvider = "gemini" };

        var effective = SettingsMerger.Merge(config, new StudioSettingsData());

        Assert.Equal("gemini-2.5-flash", effective.LlmModel);
    }

    [Fact]
    public void personaJson由来の値の上にstudioJsonが勝つ()
    {
        // ApplyPersona 済み (= persona.json の speakerId が反映済み) の AppConfig を想定
        var manifest = new PersonaManifest
        {
            SchemaVersion = 1,
            Name = "テスト",
            Slug = "test",
            Voice = new PersonaVoice { SpeakerId = 47, EmotionStyles = new Dictionary<string, int>() },
        };
        var config = new AppConfig().ApplyPersona(manifest);
        Assert.Equal(47, config.SpeakerId); // persona.json が env 無しでは勝っている

        var effective = SettingsMerger.Merge(config, new StudioSettingsData { SpeakerId = 5 });

        Assert.Equal(5, effective.SpeakerId); // その上に studio.json が勝つ
    }
}
