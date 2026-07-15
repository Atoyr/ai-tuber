using Medoz.Studio.Settings;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// <see cref="StudioSettingsStore"/> (data/studio.json) のテスト。
/// 「UI で触った項目だけを保存する」(全スナップショットにしない) ことを確認する。
/// </summary>
public class StudioSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "studio-settings-tests-" + Guid.NewGuid());
    private string JsonPath => Path.Combine(_dir, "studio.json");

    public StudioSettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 一時ディレクトリの削除失敗は無視
        }
    }

    [Fact]
    public void 触った項目だけがファイルに書かれる()
    {
        var store = new StudioSettingsStore(JsonPath);

        store.Update(data => data.SpeakerId = 8);

        string json = File.ReadAllText(JsonPath);
        Assert.Contains("\"speakerId\": 8", json);
        Assert.DoesNotContain("outputDevice", json);   // 触っていない項目は書かない (null は省略)
        Assert.DoesNotContain("freetalkAfterSec", json);
    }

    [Fact]
    public void 保存した設定は再ロードで復元される()
    {
        var store = new StudioSettingsStore(JsonPath);
        store.Update(data =>
        {
            data.FreetalkAfterSec = 90;
            data.OutputDevice = "VoiceMeeter";
        });

        var reloaded = new StudioSettingsStore(JsonPath).Current();

        Assert.Equal(90, reloaded.FreetalkAfterSec);
        Assert.Equal("VoiceMeeter", reloaded.OutputDevice);
        Assert.Null(reloaded.SpeakerId); // 触っていない項目は未設定のまま
    }

    [Fact]
    public void ファイルが無ければ空の設定から始まる()
    {
        var store = new StudioSettingsStore(JsonPath);

        var data = store.Current();

        Assert.Null(data.SpeakerId);
        Assert.Null(data.CommentBatchSec);
        Assert.Null(data.PersonaDir);
    }

    [Fact]
    public void 壊れたJSONは無視して空の設定から始まる()
    {
        File.WriteAllText(JsonPath, "{not-json");

        var store = new StudioSettingsStore(JsonPath);

        Assert.Null(store.Current().SpeakerId);
    }

    [Fact]
    public void CurrentはスナップショットでありUpdate以外で内部状態が変わらない()
    {
        var store = new StudioSettingsStore(JsonPath);
        store.Update(data => data.SpeakerId = 3);

        var snapshot = store.Current();
        snapshot.SpeakerId = 999; // スナップショットを書き換えても

        Assert.Equal(3, store.Current().SpeakerId); // 内部状態は変わらない
    }
}
