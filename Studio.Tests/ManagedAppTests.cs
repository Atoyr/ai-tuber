using Medoz.Studio.Apps;
using Xunit;

namespace Medoz.Studio.Tests;

/// <summary>
/// <see cref="ManagedApp"/> の状態遷移テスト (docs/studio-architecture.md の状態機械)。
/// プロセス操作・HTTP 死活確認は <see cref="FakeProcessRunner"/> で注入する。
/// </summary>
public class ManagedAppTests
{
    /// <summary>テスト用のフェイク IProcessRunner。挙動をフィールドで差し替える。</summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public string? Version;                     // TryGetVersionAsync の応答 (null = 応答なし)
        public bool ExternalProcessRunning;         // プロセス名一致の外部プロセスがあるか
        public bool StartThrows;                    // Start が失敗するか
        public int NextPid = 100;
        public readonly HashSet<int> AlivePids = new();
        public readonly List<int> KilledPids = new();
        public int StartCallCount;
        public int DelayCallCount;

        /// <summary>この回数 DelayAsync が呼ばれたら Version を versionAfterDelay にする (起動待ちの再現)</summary>
        public int? BecomeAliveAfterDelays;
        public string VersionAfterDelay = "0.99.9";

        public int Start(string exePath)
        {
            StartCallCount++;
            if (StartThrows)
            {
                throw new InvalidOperationException("起動失敗 (テスト)");
            }
            int pid = NextPid++;
            AlivePids.Add(pid);
            return pid;
        }

        public bool IsAlive(int processId) => AlivePids.Contains(processId);

        public void Kill(int processId)
        {
            KilledPids.Add(processId);
            AlivePids.Remove(processId);
        }

        public bool IsProcessRunningByName(string processName) => ExternalProcessRunning;

        public Task<string?> TryGetVersionAsync(string url, CancellationToken ct)
            => Task.FromResult(Version);

        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            DelayCallCount++;
            if (BecomeAliveAfterDelays is int n && DelayCallCount >= n)
            {
                Version = VersionAfterDelay;
            }
            return Task.CompletedTask;
        }
    }

    private static ManagedAppConfig HttpConfig(string? exePath, int timeoutSec = 60) => new()
    {
        DisplayName = "VOICEVOX",
        ExePathProvider = () => exePath,
        LivenessMode = LivenessMode.Http,
        HealthUrl = "http://127.0.0.1:50021/version",
        StartTimeoutSec = timeoutSec,
        PollIntervalSec = 2,
    };

    private static ManagedAppConfig ProcessConfig(string? exePath) => new()
    {
        DisplayName = "PuruPuruPNGTuber",
        ExePathProvider = () => exePath,
        LivenessMode = LivenessMode.Process,
        ProcessName = "PuruPuruPNGTuber",
    };

    /// <summary>PuruPuruPNGTuber 相当: HTTP 死活だが応答は HTML なのでバージョン表示しない。</summary>
    private static ManagedAppConfig PurupuruConfig(string? path) => new()
    {
        DisplayName = "PuruPuruPNGTuber",
        ExePathProvider = () => path,
        LivenessMode = LivenessMode.Http,
        HealthUrl = "http://127.0.0.1:8223/",
        ReportsVersion = false,
        StartTimeoutSec = 30,
        PollIntervalSec = 2,
    };

    // --- NotConfigured ---

    [Fact]
    public async Task exeパス未設定ならNotConfigured()
    {
        var runner = new FakeProcessRunner();
        var app = new ManagedApp(runner, HttpConfig(exePath: null));

        var status = await app.RefreshAsync(CancellationToken.None);

        Assert.Equal(AppState.NotConfigured, status.State);
    }

    [Fact]
    public async Task exeパス未設定でStartすると例外でNotConfiguredのまま()
    {
        var runner = new FakeProcessRunner();
        var app = new ManagedApp(runner, HttpConfig(exePath: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(CancellationToken.None));

        Assert.Equal(AppState.NotConfigured, app.Current().State);
        Assert.Equal(0, runner.StartCallCount);
    }

    [Fact]
    public async Task exeパス未設定でも外部起動を検出したらRunningExternal()
    {
        // パス未設定でも /version が応答すれば「起動済み (外部)」と表示する
        var runner = new FakeProcessRunner { Version = "0.20.0" };
        var app = new ManagedApp(runner, HttpConfig(exePath: null));

        var status = await app.RefreshAsync(CancellationToken.None);

        Assert.Equal(AppState.RunningExternal, status.State);
        Assert.Equal("0.20.0", status.Version);
    }

    // --- 起動成功 ---

    [Fact]
    public async Task Http版_起動して応答が返ればRunning()
    {
        var runner = new FakeProcessRunner { BecomeAliveAfterDelays = 2 };
        var app = new ManagedApp(runner, HttpConfig("C:/vv/run.exe"));

        var status = await app.StartAsync(CancellationToken.None);

        Assert.Equal(AppState.Running, status.State);
        Assert.Equal("0.99.9", status.Version);
        Assert.Equal(1, runner.StartCallCount);
        Assert.True(app.OwnsProcess);
    }

    [Fact]
    public async Task Process版_起動してプロセスが生きていればRunning()
    {
        var runner = new FakeProcessRunner();
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));

        var status = await app.StartAsync(CancellationToken.None);

        Assert.Equal(AppState.Running, status.State);
        Assert.True(app.OwnsProcess);
    }

    [Fact]
    public async Task ReportsVersionがfalseなら起動成功してもバージョンを持たない()
    {
        // PuruPuru のローカルサーバはトップページの HTML が返るため、本文をバージョンとして扱わない
        var runner = new FakeProcessRunner { BecomeAliveAfterDelays = 1, VersionAfterDelay = "<!DOCTYPE html>..." };
        var app = new ManagedApp(runner, PurupuruConfig("C:/pp/run_local_server.bat"));

        var status = await app.StartAsync(CancellationToken.None);

        Assert.Equal(AppState.Running, status.State);
        Assert.Null(status.Version);
    }

    [Fact]
    public async Task ReportsVersionがfalseの外部起動検出でもバージョンを持たない()
    {
        // 手動でローカルサーバを立てていた場合 → RunningExternal (殺さない対象)
        var runner = new FakeProcessRunner { Version = "<!DOCTYPE html>..." };
        var app = new ManagedApp(runner, PurupuruConfig(path: null));

        var status = await app.RefreshAsync(CancellationToken.None);

        Assert.Equal(AppState.RunningExternal, status.State);
        Assert.Null(status.Version);
    }

    // --- 外部起動検出 ---

    [Fact]
    public async Task Http版_すでに応答があれば起動せずRunningExternal()
    {
        var runner = new FakeProcessRunner { Version = "0.20.0" };
        var app = new ManagedApp(runner, HttpConfig("C:/vv/run.exe"));

        var status = await app.StartAsync(CancellationToken.None);

        Assert.Equal(AppState.RunningExternal, status.State);
        Assert.Equal(0, runner.StartCallCount); // すでに起動していたら起動しない
        Assert.False(app.OwnsProcess);
    }

    [Fact]
    public async Task Process版_プロセス名一致の外部プロセスがあれば起動せずRunningExternal()
    {
        var runner = new FakeProcessRunner { ExternalProcessRunning = true };
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));

        var status = await app.StartAsync(CancellationToken.None);

        Assert.Equal(AppState.RunningExternal, status.State);
        Assert.Equal(0, runner.StartCallCount);
    }

    // --- 停止 (自分が起動した時のみ Kill) ---

    [Fact]
    public async Task 自分が起動したプロセスはStopでKillされStoppedになる()
    {
        var runner = new FakeProcessRunner();
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));
        await app.StartAsync(CancellationToken.None);

        var status = await app.StopAsync();

        Assert.Equal(AppState.Stopped, status.State);
        Assert.Single(runner.KilledPids);
        Assert.False(app.OwnsProcess);
    }

    [Fact]
    public async Task 外部起動のプロセスはStopしてもKillしない()
    {
        var runner = new FakeProcessRunner { ExternalProcessRunning = true };
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));
        await app.RefreshAsync(CancellationToken.None); // RunningExternal を検出

        var status = await app.StopAsync();

        Assert.Empty(runner.KilledPids); // ユーザーが手起動したアプリを Studio が殺さない
        Assert.Equal(AppState.RunningExternal, status.State);
    }

    // --- 起動タイムアウト・失敗 ---

    [Fact]
    public async Task Http版_タイムアウトまで応答が無ければFaulted()
    {
        var runner = new FakeProcessRunner(); // Version = null のまま = 永遠に応答なし
        var app = new ManagedApp(runner, HttpConfig("C:/vv/run.exe", timeoutSec: 6)); // 6秒/2秒間隔 = 3回試行

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(CancellationToken.None));

        Assert.Contains("6 秒", ex.Message);
        Assert.Equal(AppState.Faulted, app.Current().State);
    }

    [Fact]
    public async Task ProcessStartの失敗はFaultedになり例外メッセージが伝わる()
    {
        var runner = new FakeProcessRunner { StartThrows = true };
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync(CancellationToken.None));

        Assert.Contains("起動失敗", ex.Message);
        Assert.Equal(AppState.Faulted, app.Current().State);
    }

    // --- 死活の追従 ---

    [Fact]
    public async Task 自分が起動したプロセスが死んだらRefreshでStoppedに戻る()
    {
        var runner = new FakeProcessRunner();
        var app = new ManagedApp(runner, ProcessConfig("C:/pp/purupuru.exe"));
        await app.StartAsync(CancellationToken.None);

        runner.AlivePids.Clear(); // プロセスが外部要因で死んだ
        var status = await app.RefreshAsync(CancellationToken.None);

        Assert.Equal(AppState.Stopped, status.State);
        Assert.False(app.OwnsProcess);
    }

    [Fact]
    public async Task 外部起動が消えたらRefreshでStoppedに戻る()
    {
        var runner = new FakeProcessRunner { Version = "0.20.0" };
        var app = new ManagedApp(runner, HttpConfig("C:/vv/run.exe"));
        await app.RefreshAsync(CancellationToken.None);
        Assert.Equal(AppState.RunningExternal, app.Current().State);

        runner.Version = null; // 外部の VOICEVOX が終了した
        var status = await app.RefreshAsync(CancellationToken.None);

        Assert.Equal(AppState.Stopped, status.State);
    }
}
