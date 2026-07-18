using System.IO;
using Medoz.AiTuber.Core;

namespace Medoz.Setup.Services;

/// <summary>
/// PERSONA_DIR が指すペルソナパッケージの検証と要約 (画面の「現在のペルソナ」表示用)。
/// UI 非依存の純粋ロジックとして切り出しユニットテスト対象にする。
/// </summary>
public static class PersonaInspector
{
    /// <summary>PERSONA_DIR 未設定時に各アプリが使う同梱サンプル (AppConfig の既定値と同じ)。</summary>
    public const string DefaultPersonaDir = "personas/default";

    /// <summary>ペルソナパッケージのモード別プロンプト (使うモードの分だけあればよい)。</summary>
    private static readonly string[] ModeFiles =
        ["live_system.md", "tweet_system.md", "game_system.md", "blog_system.md"];

    /// <summary>
    /// PERSONA_DIR の値を実行時と同じ基準で絶対パスにする。
    /// 相対パスは各アプリの実行ディレクトリ = リポジトリルート基準 (baseDir が null なら CWD)。
    /// 空欄は未設定 = 同梱サンプルとして扱う。
    /// </summary>
    public static string ResolveDir(string? personaDir, string? baseDir)
    {
        string dir = string.IsNullOrWhiteSpace(personaDir) ? DefaultPersonaDir : personaDir.Trim();
        if (Path.IsPathRooted(dir))
        {
            return Path.GetFullPath(dir);
        }
        return Path.GetFullPath(Path.Combine(baseDir ?? Directory.GetCurrentDirectory(), dir));
    }

    /// <summary>
    /// ペルソナをロードして要約を返す。ロード失敗時は false を返し、
    /// summary に原因 (不足ファイル名と場所を含む PersonaLoadException のメッセージ) を入れる。
    /// </summary>
    public static bool TryInspect(string resolvedDir, out string summary)
    {
        try
        {
            summary = Describe(PersonaPackage.Load(resolvedDir));
            return true;
        }
        catch (PersonaLoadException ex)
        {
            summary = ex.Message;
            return false;
        }
    }

    /// <summary>ロード済みペルソナの要約 (名前・声・モード別md・知識ファイル) を組み立てる。</summary>
    public static string Describe(PersonaPackage package)
    {
        var manifest = package.Manifest;
        var lines = new[]
        {
            $"名前: {manifest.Name} (slug: {manifest.Slug})",
            $"場所: {Path.GetFullPath(package.DirectoryPath)}",
            $"話者ID: {manifest.Voice!.SpeakerId} / 感情スタイル: {DescribeEmotionStyles(manifest.Voice.EmotionStyles)}",
            $"追加禁止ワード: {manifest.BannedWords?.Count ?? 0} 件",
            $"モード: {DescribeModes(package.DirectoryPath)}",
            $"知識 (knowledge/): {DescribeKnowledge(package)}",
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeEmotionStyles(IReadOnlyDictionary<string, int>? styles)
    {
        if (styles is null || styles.Count == 0)
        {
            return "(なし)";
        }
        return string.Join(", ", styles.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                       .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static string DescribeModes(string personaDir)
    {
        var available = ModeFiles.Where(f => File.Exists(Path.Combine(personaDir, f))).ToList();
        var missing = ModeFiles.Except(available).ToList();
        string s = available.Count > 0 ? string.Join(", ", available) : "(なし)";
        if (missing.Count > 0)
        {
            s += $" (未対応: {string.Join(", ", missing)})";
        }
        return s;
    }

    private static string DescribeKnowledge(PersonaPackage package)
    {
        var names = package.ListKnowledge();
        return names.Count > 0 ? string.Join(", ", names) : "(なし)";
    }
}
