using Medoz.AiTuber.Core;

namespace Medoz.Studio.Apps;

/// <summary>
/// VOICEVOX と PuruPuruPNGTuber の起動・停止・状態表示をまとめる (docs/studio-architecture.md)。
/// 起動パスは環境変数 (VOICEVOX_EXE_PATH / PURUPURU_PATH) から解決する。
/// PuruPuruPNGTuber は exe ではなくブラウザで動く Web アプリのため、起動対象は同梱の
/// run_local_server.bat (ローカルサーバ)。死活は VOICEVOX 同様、HTTP 応答
/// (既定 http://127.0.0.1:8223/、PURUPURU_URL で変更可) で判定する。
/// </summary>
public sealed class AppLauncher
{
    /// <summary>VOICEVOX 起動用の環境変数名。</summary>
    public const string VoicevoxExeEnv = "VOICEVOX_EXE_PATH";

    /// <summary>PuruPuruPNGTuber のローカルサーバ起動用の環境変数名 (run_local_server.bat のパス)。</summary>
    public const string PurupuruPathEnv = "PURUPURU_PATH";

    /// <summary>PuruPuruPNGTuber のローカルサーバ URL の環境変数名。</summary>
    public const string PurupuruUrlEnv = "PURUPURU_URL";

    /// <summary>PuruPuruPNGTuber のローカルサーバの既定 URL。</summary>
    public const string PurupuruDefaultUrl = "http://127.0.0.1:8223";

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

        PurupuruUrl = (EnvOrNull(PurupuruUrlEnv) ?? PurupuruDefaultUrl).TrimEnd('/');
        Purupuru = new ManagedApp(runner, new ManagedAppConfig
        {
            DisplayName = "PuruPuruPNGTuber",
            ExePathProvider = () => EnvOrNull(PurupuruPathEnv),
            LivenessMode = LivenessMode.Http,
            HealthUrl = PurupuruUrl + "/",
            ReportsVersion = false,      // トップページの HTML が返るのでバージョンとしては扱わない
            StartTimeoutSec = 30,
            PollIntervalSec = 2,
        });
    }

    /// <summary>PuruPuruPNGTuber のローカルサーバ URL (UI の「画面を開く」リンクに使う)。</summary>
    public string PurupuruUrl { get; }

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
}
