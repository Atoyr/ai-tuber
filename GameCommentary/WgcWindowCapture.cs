using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Medoz.GameCommentary;

/// <summary>
/// Windows.Graphics.Capture (WGC) で対象ウィンドウをキャプチャする実装。
/// OBS の「キャプチャ方法: Windows 10 (1903以降)」と同じ方式で、
/// <see cref="WindowCapture"/> (PrintWindow) が撮れないケースを撮れる:
///
/// - **管理者権限で動くウィンドウ**: PrintWindow は WM_PRINT を送るため UIPI に阻まれ
///   ERROR_ACCESS_DENIED(5) になるが、WGC はコンポジタから合成済みの絵をもらうので影響を受けない
/// - **他ウィンドウに隠れているウィンドウ**: 画面からの BitBlt と違い手前のウィンドウが写り込まない
/// - **GPU (DirectX/Unity) 描画**: 黒画像にならない
///
/// 要件は Windows 10 1903+ (TFM の 10.0.19041.0 はこのため)。
/// 実行環境が未対応の場合はコンストラクタで明確な例外にする。
/// </summary>
[SupportedOSPlatform("windows10.0.19041")]
public sealed class WgcWindowCapture : IWindowCapture, IDisposable
{
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>1フレーム到着を待つ上限。これを超えたらキャプチャ失敗として扱う。</summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);

    private readonly GraphicsCaptureItem _item;
    private readonly IDirect3DDevice _device;
    private readonly int _maxWidth;
    private bool _disposed;

    public string TargetTitle { get; }

    /// <summary>
    /// タイトルに <paramref name="titleFragment"/> を含む最初の可視ウィンドウを対象にする。
    /// 見つからなければ候補一覧付きの <see cref="InvalidOperationException"/> (PrintWindow 版と同じ UX)。
    /// </summary>
    public WgcWindowCapture(string titleFragment, int maxWidth = 800)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new InvalidOperationException(
                "この環境では Windows.Graphics.Capture が利用できません (Windows 10 1903 以降が必要です)。\n" +
                $"CAPTURE_METHOD={WindowCaptureFactory.PrintWindow} で従来方式に切り替えられます。");
        }

        _maxWidth = maxWidth;
        (IntPtr hWnd, string title) = WindowFinder.Find(titleFragment);
        TargetTitle = title;
        _item = CreateItemForWindow(hWnd);
        _device = Interop.CreateDirect3DDevice();
    }

    /// <inheritdoc />
    /// <remarks>
    /// WGC は本質的に非同期 (フレーム到着を待つ) だが、<see cref="IWindowCapture"/> の契約に合わせて同期で返す。
    /// 呼び出し元 (CommentaryLoop) は SynchronizationContext の無いスレッドプール上で動くためデッドロックしない。
    /// </remarks>
    public byte[] CaptureJpeg()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CaptureJpegAsync().GetAwaiter().GetResult();
    }

    private async Task<byte[]> CaptureJpegAsync()
    {
        // フレームプールとセッションはキャプチャごとに作る。12秒間隔 (CAPTURE_INTERVAL_SEC) では
        // オーバーヘッドが無視でき、対象ウィンドウのリサイズにも自然に追従できる。
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device, DirectXPixelFormat.B8G8R8A8UIntNormalized, numberOfBuffers: 2, _item.Size);
        using var session = framePool.CreateCaptureSession(_item);

        var frameArrived = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        framePool.FrameArrived += (pool, _) =>
        {
            var frame = pool.TryGetNextFrame();
            if (frame is not null && !frameArrived.TrySetResult(frame))
            {
                frame.Dispose(); // 最初の1枚だけ使う。以降は捨てる
            }
        };

        session.StartCapture();

        var completed = await Task.WhenAny(frameArrived.Task, Task.Delay(FrameTimeout));
        if (completed != frameArrived.Task)
        {
            throw new InvalidOperationException(
                $"WGC のフレームが {FrameTimeout.TotalSeconds} 秒以内に届きませんでした " +
                $"(対象: '{TargetTitle}')。ウィンドウが最小化されていませんか?");
        }

        using Direct3D11CaptureFrame capturedFrame = await frameArrived.Task;
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
            capturedFrame.Surface, BitmapAlphaMode.Premultiplied);

        return await EncodeJpegAsync(bitmap, _maxWidth);
    }

    /// <summary>
    /// 幅 maxWidth に縮小して JPEG 品質80でエンコードする
    /// (PrintWindow 版・Python版と同じ「幅800px / quality=80」の動作仕様)。
    /// </summary>
    private static async Task<byte[]> EncodeJpegAsync(SoftwareBitmap bitmap, int maxWidth)
    {
        (int targetWidth, int targetHeight) = ImageScaling.FitWidth(bitmap.PixelWidth, bitmap.PixelHeight, maxWidth);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.JpegEncoderId, stream,
            new[] { new KeyValuePair<string, BitmapTypedValue>("ImageQuality", new BitmapTypedValue(0.8d, Windows.Foundation.PropertyType.Single)) });

        // JPEG はアルファを持たないため、乗算済みアルファのまま渡すと encoder に拒否される
        using var opaque = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        encoder.SetSoftwareBitmap(opaque);
        encoder.BitmapTransform.ScaledWidth = (uint)targetWidth;
        encoder.BitmapTransform.ScaledHeight = (uint)targetHeight;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>HWND から GraphicsCaptureItem を作る (WinRT の interop 経由。C# からは直接作れない)。</summary>
    private static GraphicsCaptureItem CreateItemForWindow(IntPtr hWnd)
    {
        var interop = (IGraphicsCaptureItemInterop)Interop.GetActivationFactory(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            typeof(IGraphicsCaptureItemInterop).GUID);

        Guid iid = GraphicsCaptureItemIid;
        IntPtr itemPtr = interop.CreateForWindow(hWnd, ref iid);
        return GraphicsCaptureItem.FromAbi(itemPtr);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _device.Dispose();
    }
}

/// <summary>HWND / HMONITOR から GraphicsCaptureItem を作る WinRT interop インターフェース。</summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow(IntPtr window, ref Guid iid);
    IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
}
