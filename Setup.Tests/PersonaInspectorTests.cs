using Medoz.Setup.Services;

namespace Medoz.Setup.Tests;

public class PersonaInspectorTests : IDisposable
{
    private readonly string _personaDir;

    private const string ValidManifest = """
        {
          "schemaVersion": 1,
          "name": "テスト",
          "slug": "test",
          "voice": { "speakerId": 8, "emotionStyles": { "joy": 1, "fun": 3 } },
          "bannedWords": ["追加ワード"]
        }
        """;

    public PersonaInspectorTests()
    {
        _personaDir = Path.Combine(Path.GetTempPath(), "aituber-setup-persona-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_personaDir);
        File.WriteAllText(Path.Combine(_personaDir, "persona.json"), ValidManifest);
        File.WriteAllText(Path.Combine(_personaDir, "character.md"), "あなたはテストです。");
        File.WriteAllText(Path.Combine(_personaDir, "live_system.md"), "配信モードの指示。");
        File.WriteAllText(Path.Combine(_personaDir, "game_system.md"), "実況モードの指示。");
        Directory.CreateDirectory(Path.Combine(_personaDir, "knowledge"));
        File.WriteAllText(Path.Combine(_personaDir, "knowledge", "sample-game.md"), "ゲームの知識。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_personaDir))
        {
            Directory.Delete(_personaDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveDir_AbsolutePath_IsReturnedAsIs()
    {
        string resolved = PersonaInspector.ResolveDir(_personaDir, @"C:\somewhere-else");

        Assert.Equal(Path.GetFullPath(_personaDir), resolved);
    }

    [Fact]
    public void ResolveDir_RelativePath_ResolvesFromBaseDir()
    {
        string baseDir = Path.GetDirectoryName(_personaDir)!;
        string name = Path.GetFileName(_personaDir);

        string resolved = PersonaInspector.ResolveDir("../" + name, Path.Combine(baseDir, "repo"));

        Assert.Equal(Path.GetFullPath(_personaDir), resolved);
    }

    [Fact]
    public void ResolveDir_Empty_UsesBundledDefault()
    {
        string resolved = PersonaInspector.ResolveDir("", @"C:\repo");

        Assert.Equal(Path.GetFullPath(@"C:\repo\personas\default"), resolved);
    }

    [Fact]
    public void TryInspect_ValidPersona_ReturnsSummary()
    {
        bool ok = PersonaInspector.TryInspect(_personaDir, out string summary);

        Assert.True(ok);
        Assert.Contains("テスト", summary);
        Assert.Contains("test", summary);
        Assert.Contains("8", summary);
        Assert.Contains("joy=1", summary);
        Assert.Contains("live_system.md", summary);
        Assert.Contains("sample-game", summary);
        // 無いモードは「未対応」として見える
        Assert.Contains("未対応", summary);
        Assert.Contains("tweet_system.md", summary);
    }

    [Fact]
    public void TryInspect_MissingManifest_ReturnsError()
    {
        File.Delete(Path.Combine(_personaDir, "persona.json"));

        bool ok = PersonaInspector.TryInspect(_personaDir, out string summary);

        Assert.False(ok);
        Assert.Contains("persona.json", summary);
    }
}
