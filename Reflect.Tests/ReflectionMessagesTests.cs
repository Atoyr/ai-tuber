using Medoz.AiTuber.Core;
using Medoz.Reflect;

namespace Medoz.Reflect.Tests;

public class ReflectionMessagesTests
{
    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0);

    [Fact]
    public void BuildContext_IncludesCharacterPromptAndInstruction()
    {
        string context = ReflectionMessages.BuildContext(new MemoryData(), "あなたはサンプルです。", Now);

        Assert.Contains("現在日時: 2026-07-15 12:00", context);
        Assert.Contains("現在の人格設定", context);
        Assert.Contains("あなたはサンプルです。", context);
        Assert.EndsWith("指示されたJSON形式で振り返りと人格提案を出力してください。", context);
    }

    [Fact]
    public void BuildContext_NotesEmptyMemory()
    {
        string context = ReflectionMessages.BuildContext(new MemoryData(), "人格", Now);

        Assert.Contains("まだ記憶がありません", context);
        Assert.DoesNotContain("## 配信メモ", context);
    }

    [Fact]
    public void BuildContext_IncludesAllStreamNotesAndTweetsAndPosts()
    {
        var memory = new MemoryData
        {
            StreamNotes =
            {
                new StreamNote("2026-07-01 20:00", "古い配信"),
                new StreamNote("2026-07-07 21:00", "レトロゲーム回"),
            },
            RecentTweets = { "つぶやきA", "つぶやきB" },
            RecentPosts = { new PostNote("2026-07-05 10:00", "はじめての記事") },
        };

        string context = ReflectionMessages.BuildContext(memory, "人格", Now);

        // 配信メモは最新1件ではなく全件渡す(ツイート生成との違い)
        Assert.Contains("古い配信", context);
        Assert.Contains("レトロゲーム回", context);
        Assert.Contains("つぶやきA", context);
        Assert.Contains("はじめての記事", context);
        Assert.DoesNotContain("まだ記憶がありません", context);
    }

    [Theory]
    [InlineData("{\"observations\": [\"気づき\"], \"proposals\": [{\"section\": \"性格の核\", \"add\": \"文\", \"reason\": \"根拠\"}]}")]
    [InlineData("```json\n{\"observations\": [], \"proposals\": []}\n```")]
    public void Parse_AcceptsValidJson(string raw)
    {
        Assert.NotNull(ReflectionMessages.Parse(raw));
    }

    [Fact]
    public void Parse_ExtractsFields()
    {
        string raw = "{\"observations\": [\"よく笑う\"], \"proposals\": [{\"section\": \"性格の核\", \"add\": \"よく笑う\", \"reason\": \"配信メモより\"}]}";

        ReflectionResult? result = ReflectionMessages.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal("よく笑う", result!.Observations[0]);
        Assert.Equal("性格の核", result.Proposals[0].Section);
        Assert.Equal("よく笑う", result.Proposals[0].Add);
        Assert.Equal("配信メモより", result.Proposals[0].Reason);
    }

    [Theory]
    [InlineData("これはJSONではない")]
    [InlineData("{\"observations\": [\"気づき\"]}")]                                     // proposals 欠落
    [InlineData("{\"proposals\": []}")]                                                 // observations 欠落
    [InlineData("{\"observations\": [], \"proposals\": [{\"section\": \"\", \"add\": \"文\", \"reason\": \"r\"}]}")] // section 空
    [InlineData("{\"observations\": [], \"proposals\": [{\"section\": \"核\", \"add\": \"\", \"reason\": \"r\"}]}")]   // add 空
    public void Parse_ReturnsNull_OnInvalidInput(string raw)
    {
        Assert.Null(ReflectionMessages.Parse(raw));
    }

    [Fact]
    public void AllTexts_YieldsObservationsAndProposalFields()
    {
        var result = new ReflectionResult(
            new[] { "気づき1" },
            new[] { new PersonaProposal("見出し", "追記文", "理由文") });

        var texts = ReflectionMessages.AllTexts(result).ToList();

        Assert.Contains("気づき1", texts);
        Assert.Contains("見出し", texts);
        Assert.Contains("追記文", texts);
        Assert.Contains("理由文", texts);
    }

    [Fact]
    public void RenderMarkdown_IncludesProposals()
    {
        var result = new ReflectionResult(
            new[] { "常連が多い" },
            new[] { new PersonaProposal("性格の核", "常連に名前で反応する", "配信メモより") });

        string md = ReflectionMessages.RenderMarkdown(result, "サンプル", Now);

        Assert.Contains("# 人格提案 (2026-07-15 12:00) — サンプル", md);
        Assert.Contains("character.md を書き換えません", md);
        Assert.Contains("- 常連が多い", md);
        Assert.Contains("### 性格の核", md);
        Assert.Contains("常連に名前で反応する", md);
    }

    [Fact]
    public void RenderMarkdown_HandlesNoProposals()
    {
        var result = new ReflectionResult(Array.Empty<string>(), Array.Empty<PersonaProposal>());

        string md = ReflectionMessages.RenderMarkdown(result, "サンプル", Now);

        Assert.Contains("足すに値する提案はありませんでした", md);
    }
}
