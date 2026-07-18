using Medoz.AiTuber.Core;
using Medoz.Reflect;

namespace Medoz.Reflect.Tests;

public class InterviewMessagesTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 12, 0, 0);

    [Fact]
    public void BuildOpening_IncludesCharacterPromptAndInstruction()
    {
        string opening = InterviewMessages.BuildOpening("あなたはサンプルです。", new MemoryData(), Now);

        Assert.Contains("現在日時: 2026-07-18 12:00", opening);
        Assert.Contains("現在の人格設定", opening);
        Assert.Contains("あなたはサンプルです。", opening);
        Assert.EndsWith("最初の質問をしてください。", opening);
    }

    [Fact]
    public void BuildOpening_OmitsStreamNotesSection_WhenMemoryEmpty()
    {
        string opening = InterviewMessages.BuildOpening("人格", new MemoryData(), Now);

        Assert.DoesNotContain("直近の配信メモ", opening);
    }

    [Fact]
    public void BuildOpening_IncludesOnlyRecentStreamNotes()
    {
        var memory = new MemoryData();
        for (int i = 1; i <= 7; i++)
        {
            memory.StreamNotes.Add(new StreamNote($"2026-07-{i:00} 20:00", $"配信{i}"));
        }

        string opening = InterviewMessages.BuildOpening("人格", memory, Now);

        // 直近5件だけ(古い2件は含めない)
        Assert.DoesNotContain("配信1", opening);
        Assert.DoesNotContain("配信2", opening);
        Assert.Contains("配信3", opening);
        Assert.Contains("配信7", opening);
    }

    [Fact]
    public void Parse_AcceptsReplacesField()
    {
        string raw = "{\"observations\": [], \"proposals\": [{\"section\": \"性格の核\", " +
                     "\"add\": \"修正後の文\", \"replaces\": \"元の文\", \"reason\": \"会話より\"}]}";

        ReflectionResult? result = ReflectionMessages.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal("元の文", result!.Proposals[0].Replaces);
    }

    [Fact]
    public void RenderMarkdown_ShowsReplaceTarget_ForRevisionProposal()
    {
        var result = new ReflectionResult(
            Array.Empty<string>(),
            new[] { new PersonaProposal("性格の核", "修正後の文", "会話より", Replaces: "元の文") });

        string md = ReflectionMessages.RenderMarkdown(result, "サンプル", Now);

        Assert.Contains("**修正案**: 修正後の文", md);
        Assert.Contains("置き換え対象: 元の文", md);
        Assert.DoesNotContain("追記案", md);
    }
}
