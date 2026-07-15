using Medoz.AiTuber.Core;

namespace Medoz.Studio.Apps;

/// <summary>
/// VOICEVOX と PuruPuruPNGTuber の起動・停止・状態表示をまとめる (docs/studio-architecture.md)。
/// exe パスは環境変数 (VOICEVOX_EXE_PATH / PURUPURU_EXE_PATH) から解決する。
/// VOICEVOX の死活は <c>/version</c> ポーリング、PuruPuru はプロセス生存で判定する。
/// </summary>
public sealed class AppLauncher
{
    /// <summary>VOICEVOX 起動用の環境変数名。</summary>
    public const string VoicevoxExeEnv = "VOICEVOX_EXE_PATH";

    /// <summary>PuruPuruPNGTuber 起動用の環境変数名。</summary>
    public const string PurupuruExeEnv = "PURUPURU_EXE_PATH";

    /// <summary>PuruPuru の外部プロセス検出に使う既定のプロセス名。</summary>
    public const string PurupuruDefaultProcessName = "PuruPuruPNGTuber";

    public ManagedApp Voicevox { get; }
    public ManagedApp Purupuru { get; }

    public AppLauncher(IProcessRunner runner, Func<AppConfig> configProvider)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(configProvider);

        Voicevox = new ManagedApp(runner, new ManagedAppConfig
        {
            DisplayName = "VOICEVOX",
            ExePathProvider = () => EnvOrNull(VoicevoxExeEnv),
            LivenessMode = LivenessMode.Http,
            HealthUrl = configProvider().VoicevoxUrl.TrimEnd('/') + "/version",
            StartTimeoutSec = 60,
            PollIntervalSec = 2,
        });

        Purupuru = new ManagedApp(runner, new ManagedAppConfig
        {
            DisplayName = "PuruPuruPNGTuber",
            ExePathProvider = () => EnvOrNull(PurupuruExeEnv),
            LivenessMode = LivenessMode.Process,
            ProcessName = ProcessNameFrom(EnvOrNull(PurupuruExeEnv)),
        });
    }

    public ManagedApp Get(string name) => name.ToLowerInvariant() switch
    {
        "voicevox" => Voicevox,
        "purupuru" => Purupuru,
        _ => throw new ArgumentException($"未知のアプリです: {name}", nameof(name)),
    };

    /// <summary>両アプリの死活を確認して状態を更新する。</summary>
    public async Task RefreshAllAsync(CancellationToken ct)
    {
        await Voicevox.RefreshAsync(ct);
        await Purupuru.RefreshAsync(ct);
    }

    /// <summary>まとめて起動: VOICEVOX 起動 → /version 応答待ち → PuruPuru 起動 (Live 開始まではしない)。</summary>
    public async Task StartAllAsync(CancellationToken ct)
    {
        await Voicevox.StartAsync(ct);
        await Purupuru.StartAsync(ct);
    }

    private static string? EnvOrNull(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static string ProcessNameFrom(string? exePath)
        => string.IsNullOrWhiteSpace(exePath)
            ? PurupuruDefaultProcessName
            : Path.GetFileNameWithoutExtension(exePath);
}
