using System.Diagnostics;

namespace Medoz.Studio.Apps;

/// <summary>
/// <see cref="IProcessRunner"/> の本番実装。実プロセスの起動・監視と HTTP 死活確認を行う。
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly HttpClient _http;

    public ProcessRunner(HttpClient? http = null)
    {
        // 死活確認は短めのタイムアウトで (未起動なら即失敗にしたい)
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    }

    public int Start(string exePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,                 // exe を通常起動 (作業ディレクトリはそのまま)
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"プロセスの起動に失敗しました: {exePath}");
        return process.Id;
    }

    public bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // 該当 PID のプロセスが存在しない
            return false;
        }
    }

    public void Kill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // すでに終了している
        }
    }

    public bool IsProcessRunningByName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> TryGetVersionAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            string body = (await response.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
            return body.Length > 0 ? body : "unknown";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 未起動・接続拒否・タイムアウトなどは「応答なし」として扱う
            return null;
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
