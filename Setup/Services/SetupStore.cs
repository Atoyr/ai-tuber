using System.IO;
using System.Text;

namespace Medoz.Setup.Services;

/// <summary>
/// 設定の永続化: ユーザー環境変数と PostX/.env への書き込みを行う。
/// ユーザー環境変数は次回以降に起動されたプロセスから見える (現在の PowerShell/CMD セッションからは見えない)。
/// </summary>
public static class SetupStore
{
    private const EnvironmentVariableTarget Target = EnvironmentVariableTarget.User;

    /// <summary>現在の環境変数から可能な限り値を読み込む。</summary>
    public static SetupSettings LoadCurrent()
    {
        var s = new SetupSettings();

        s.LlmProvider = Env("LLM_PROVIDER") ?? s.LlmProvider;
        s.AnthropicApiKey = Env("ANTHROPIC_API_KEY") ?? "";
        s.ClaudeModel = Env("CLAUDE_MODEL") ?? s.ClaudeModel;
        s.GeminiApiKey = Env("GEMINI_API_KEY") ?? "";
        s.GeminiModel = Env("GEMINI_MODEL") ?? s.GeminiModel;
        s.OpenAIApiKey = Env("OPENAI_API_KEY") ?? "";
        s.OpenAIModel = Env("OPENAI_MODEL") ?? s.OpenAIModel;

        s.VoicevoxUrl = Env("VOICEVOX_URL") ?? s.VoicevoxUrl;
        if (int.TryParse(Env("VOICEVOX_SPEAKER_ID"), out int sid)) s.SpeakerId = sid;
        s.OutputDeviceName = Env("VOICEVOX_OUTPUT_DEVICE") ?? s.OutputDeviceName;

        // X は AppConfig と PostX の両方式を吸収 (どちらの名前でもよい)
        s.XConsumerKey = Env("X_CONSUMER_KEY") ?? Env("X_API_KEY") ?? "";
        s.XConsumerSecret = Env("X_CONSUMER_SECRET") ?? Env("X_API_SECRET") ?? "";
        s.XAccessToken = Env("X_ACCESS_TOKEN") ?? "";
        s.XAccessTokenSecret = Env("X_ACCESS_TOKEN_SECRET") ?? Env("X_ACCESS_SECRET") ?? "";

        return s;
    }

    public sealed record SaveOptions(bool SaveLlm, bool SaveVoicevox, bool SaveX);

    public sealed record SaveReport(IReadOnlyList<string> Messages, IReadOnlyList<string> Errors)
    {
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>選択されたセクションを保存する。</summary>
    public static SaveReport Save(SetupSettings s, SaveOptions opts)
    {
        var messages = new List<string>();
        var errors = new List<string>();

        try
        {
            if (opts.SaveLlm)
            {
                SetEnv("LLM_PROVIDER", s.LlmProvider);
                SetEnvIfNotEmpty("ANTHROPIC_API_KEY", s.AnthropicApiKey);
                SetEnv("CLAUDE_MODEL", s.ClaudeModel);
                SetEnvIfNotEmpty("GEMINI_API_KEY", s.GeminiApiKey);
                SetEnv("GEMINI_MODEL", s.GeminiModel);
                SetEnvIfNotEmpty("OPENAI_API_KEY", s.OpenAIApiKey);
                SetEnv("OPENAI_MODEL", s.OpenAIModel);
                messages.Add("LLM 設定を保存しました (ユーザー環境変数)");
            }

            if (opts.SaveVoicevox)
            {
                SetEnv("VOICEVOX_URL", s.VoicevoxUrl);
                SetEnv("VOICEVOX_SPEAKER_ID", s.SpeakerId.ToString());
                SetEnv("VOICEVOX_OUTPUT_DEVICE", s.OutputDeviceName);
                messages.Add("VOICEVOX 設定を保存しました (ユーザー環境変数)");
            }

            if (opts.SaveX)
            {
                // AppConfig 互換 (X_API_KEY / X_API_SECRET / X_ACCESS_SECRET)
                SetEnvIfNotEmpty("X_API_KEY", s.XConsumerKey);
                SetEnvIfNotEmpty("X_API_SECRET", s.XConsumerSecret);
                SetEnvIfNotEmpty("X_ACCESS_TOKEN", s.XAccessToken);
                SetEnvIfNotEmpty("X_ACCESS_SECRET", s.XAccessTokenSecret);

                // PostX 互換
                SetEnvIfNotEmpty("X_CONSUMER_KEY", s.XConsumerKey);
                SetEnvIfNotEmpty("X_CONSUMER_SECRET", s.XConsumerSecret);
                SetEnvIfNotEmpty("X_ACCESS_TOKEN_SECRET", s.XAccessTokenSecret);

                // PostX/.env にも書く (PostX は DotNetEnv で読み込む)
                var repoRoot = FindRepoRoot();
                if (repoRoot is not null)
                {
                    var postxDir = Path.Combine(repoRoot, "PostX");
                    if (Directory.Exists(postxDir))
                    {
                        var envPath = Path.Combine(postxDir, ".env");
                        WriteDotEnv(envPath, s);
                        messages.Add($"PostX の .env を書き出しました: {envPath}");
                    }
                }
                messages.Add("X 認証情報を保存しました (ユーザー環境変数)");
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return new SaveReport(messages, errors);
    }

    private static string? Env(string name)
    {
        // User → Process → Machine の順で調べる (User が最優先)
        var v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        if (!string.IsNullOrEmpty(v)) return v;
        v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrEmpty(v)) return v;
        v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static void SetEnv(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value, Target);
    }

    private static void SetEnvIfNotEmpty(string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(name, value, Target);
        }
    }

    private static void WriteDotEnv(string envPath, SetupSettings s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# X API 認証情報 (Setup ツールが自動生成)");
        sb.AppendLine($"X_CONSUMER_KEY={s.XConsumerKey}");
        sb.AppendLine($"X_CONSUMER_SECRET={s.XConsumerSecret}");
        sb.AppendLine($"X_ACCESS_TOKEN={s.XAccessToken}");
        sb.AppendLine($"X_ACCESS_TOKEN_SECRET={s.XAccessTokenSecret}");
        File.WriteAllText(envPath, sb.ToString());
    }

    /// <summary>実行時 CWD から親ディレクトリを辿って .sln があるフォルダを探す。</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
