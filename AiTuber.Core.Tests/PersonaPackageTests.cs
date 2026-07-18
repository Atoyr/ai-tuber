using Medoz.AiTuber.Core;

namespace Medoz.AiTuber.Core.Tests;

public class PersonaPackageTests : IDisposable
{
    private readonly string _personaDir;

    private const string ValidManifest = """
        {
          "schemaVersion": 1,
          "name": "テスト",
          "slug": "test",
          "voice": { "speakerId": 8, "emotionStyles": { "Joy": 1, "fun": 3 } },
          "bannedWords": ["追加ワード"]
        }
        """;

    public PersonaPackageTests()
    {
        _personaDir = Path.Combine(Path.GetTempPath(), "aituber-persona-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_personaDir);
        File.WriteAllText(Path.Combine(_personaDir, "persona.json"), ValidManifest);
        File.WriteAllText(Path.Combine(_personaDir, "character.md"), "あなたはテストです。");
        File.WriteAllText(Path.Combine(_personaDir, "live_system.md"), "配信モードの指示。");
    }

    public void Dispose()
    {
        if (Directory.Exists(_personaDir))
        {
            Directory.Delete(_personaDir, recursive: true);
        }
    }

    private void WriteManifest(string json)
        => File.WriteAllText(Path.Combine(_personaDir, "persona.json"), json);

    [Fact]
    public void Load_ParsesManifestAndCharacterPrompt()
    {
        var package = PersonaPackage.Load(_personaDir);

        Assert.Equal("テスト", package.Manifest.Name);
        Assert.Equal("test", package.Manifest.Slug);
        Assert.Equal(8, package.Manifest.Voice!.SpeakerId);
        Assert.Equal(new[] { "追加ワード" }, package.Manifest.BannedWords!);
        Assert.Equal("あなたはテストです。", package.CharacterPrompt);
    }

    [Fact]
    public void Load_NormalizesEmotionStyleKeysToLowerCase()
    {
        var package = PersonaPackage.Load(_personaDir);

        Assert.Equal(1, package.Manifest.Voice!.EmotionStyles!["joy"]);
        Assert.False(package.Manifest.Voice.EmotionStyles.ContainsKey("Joy"));
    }

    [Fact]
    public void Load_Throws_WhenDirectoryMissing()
    {
        string missing = Path.Combine(_personaDir, "no-such-dir");

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(missing));

        Assert.Contains("PERSONA_DIR", ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenManifestMissing()
    {
        File.Delete(Path.Combine(_personaDir, "persona.json"));

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));

        Assert.Contains("persona.json", ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenManifestIsBrokenJson()
    {
        WriteManifest("{ これはJSONではない");

        Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));
    }

    [Fact]
    public void Load_Throws_OnUnknownSchemaVersion()
    {
        WriteManifest("""{ "schemaVersion": 2, "name": "x", "slug": "x", "voice": { "speakerId": 1, "emotionStyles": {} } }""");

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));

        Assert.Contains("schemaVersion", ex.Message);
    }

    [Theory]
    [InlineData("")]        // 空
    [InlineData("Potofu")]  // 大文字
    [InlineData("po tofu")] // 空白
    public void Load_Throws_OnInvalidSlug(string slug)
    {
        WriteManifest($$"""{ "schemaVersion": 1, "name": "x", "slug": "{{slug}}", "voice": { "speakerId": 1, "emotionStyles": {} } }""");

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));

        Assert.Contains("slug", ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenVoiceSpeakerIdMissing()
    {
        WriteManifest("""{ "schemaVersion": 1, "name": "x", "slug": "x", "voice": { "emotionStyles": {} } }""");

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));

        Assert.Contains("speakerId", ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenCharacterMissing()
    {
        File.Delete(Path.Combine(_personaDir, "character.md"));

        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir));

        Assert.Contains("character.md", ex.Message);
    }

    [Fact]
    public void Load_Throws_WhenRequestedModeFileMissing()
    {
        var ex = Assert.Throws<PersonaLoadException>(() => PersonaPackage.Load(_personaDir, "tweet_system.md"));

        Assert.Contains("tweet_system.md", ex.Message);
    }

    [Fact]
    public void Load_Succeeds_WhenRequestedModeFileExists()
    {
        var package = PersonaPackage.Load(_personaDir, "live_system.md");

        Assert.Equal("配信モードの指示。", package.LoadModePrompt("live_system.md"));
    }

    [Fact]
    public void BuildSystemPrompt_JoinsCharacterAndModeWithSeparator()
    {
        var package = PersonaPackage.Load(_personaDir);

        string prompt = package.BuildSystemPrompt("live_system.md");

        Assert.Equal("あなたはテストです。\n\n---\n\n配信モードの指示。", prompt);
    }

    // --- knowledge/ (ゲーム知識などの任意ファイル。契約: docs/persona-architecture.md) ---

    private void WriteKnowledge(string name, string content)
    {
        string dir = Path.Combine(_personaDir, "knowledge");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".md"), content);
    }

    [Fact]
    public void ListKnowledge_ReturnsEmpty_WhenKnowledgeDirMissing()
    {
        var package = PersonaPackage.Load(_personaDir);

        Assert.Empty(package.ListKnowledge());
    }

    [Fact]
    public void ListKnowledge_ReturnsNamesWithoutExtensionSorted()
    {
        WriteKnowledge("zelda", "ゼルダの知識");
        WriteKnowledge("minecraft", "マイクラの知識");
        var package = PersonaPackage.Load(_personaDir);

        Assert.Equal(new[] { "minecraft", "zelda" }, package.ListKnowledge());
    }

    [Fact]
    public void LoadKnowledge_ReturnsFileContent()
    {
        WriteKnowledge("minecraft", "マイクラの知識。");
        var package = PersonaPackage.Load(_personaDir);

        Assert.Equal("マイクラの知識。", package.LoadKnowledge("minecraft"));
    }

    [Fact]
    public void LoadKnowledge_Throws_WithAvailableList_WhenFileMissing()
    {
        WriteKnowledge("minecraft", "マイクラの知識。");
        var package = PersonaPackage.Load(_personaDir);

        var ex = Assert.Throws<PersonaLoadException>(() => package.LoadKnowledge("zelda"));

        // どの知識が無かったか + 利用可能な一覧を含める (fail fast の UX 方針)
        Assert.Contains("zelda", ex.Message);
        Assert.Contains("minecraft", ex.Message);
    }

    [Fact]
    public void LoadKnowledge_Throws_WithClearMessage_WhenKnowledgeDirMissing()
    {
        var package = PersonaPackage.Load(_personaDir);

        var ex = Assert.Throws<PersonaLoadException>(() => package.LoadKnowledge("minecraft"));

        Assert.Contains("knowledge", ex.Message);
    }

    [Fact]
    public void BuildSystemPrompt_AppendsKnowledgeSection()
    {
        WriteKnowledge("minecraft", "マイクラの知識。");
        File.WriteAllText(Path.Combine(_personaDir, "game_system.md"), "実況モードの指示。");
        var package = PersonaPackage.Load(_personaDir);

        string prompt = package.BuildSystemPrompt("game_system.md", "minecraft");

        Assert.Equal("あなたはテストです。\n\n---\n\n実況モードの指示。\n\n---\n\nマイクラの知識。", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithoutKnowledge_KeepsLegacyOutput()
    {
        WriteKnowledge("minecraft", "マイクラの知識。");
        var package = PersonaPackage.Load(_personaDir);

        // knowledgeName 未指定 (null) なら従来と同一 (後方互換)
        Assert.Equal("あなたはテストです。\n\n---\n\n配信モードの指示。",
                     package.BuildSystemPrompt("live_system.md", null));
    }
}
