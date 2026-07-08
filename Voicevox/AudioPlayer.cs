using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Medoz.Voicevox;

/// <summary>
/// wav バイト列を指定の出力デバイスに再生する。
/// デバイスはフレンドリー名の部分一致(大文字小文字無視)で選択する。
/// </summary>
public class AudioPlayer : IDisposable
{
    private readonly MMDevice _device;

    public string DeviceName => _device.FriendlyName;

    public AudioPlayer(string deviceNameFragment)
    {
        if (string.IsNullOrEmpty(deviceNameFragment))
        {
            throw new ArgumentNullException(nameof(deviceNameFragment), "Device name fragment cannot be null or empty");
        }

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        var index = FindDeviceIndex(devices.Select(d => d.FriendlyName).ToList(), deviceNameFragment);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"'{deviceNameFragment}' を含む出力デバイスが見つかりません。\n" +
                $"利用可能: [{string.Join(", ", devices.Select(d => d.FriendlyName))}]");
        }
        _device = devices[index];
    }

    /// <summary>
    /// アクティブな出力デバイスのフレンドリー名一覧を返す。
    /// </summary>
    public static IReadOnlyList<string> GetOutputDeviceNames()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => d.FriendlyName)
            .ToList();
    }

    internal static int FindDeviceIndex(IReadOnlyList<string> deviceNames, string nameFragment)
    {
        for (var i = 0; i < deviceNames.Count; i++)
        {
            if (deviceNames[i].Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// wav バイト列を再生し、再生完了まで待機する。
    /// </summary>
    public async Task PlayWavAsync(byte[] wavData, CancellationToken cancellationToken = default)
    {
        if (wavData is null || wavData.Length == 0)
        {
            throw new ArgumentNullException(nameof(wavData), "Wav data cannot be null or empty");
        }

        using var stream = new MemoryStream(wavData);
        using var reader = new WaveFileReader(stream);
        using var output = new WasapiOut(_device, AudioClientShareMode.Shared, false, 200);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                tcs.TrySetException(e.Exception);
            }
            else
            {
                tcs.TrySetResult(null);
            }
        };

        output.Init(reader);
        output.Play();

        using (cancellationToken.Register(() => output.Stop()))
        {
            await tcs.Task;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        _device.Dispose();
        GC.SuppressFinalize(this);
    }
}
