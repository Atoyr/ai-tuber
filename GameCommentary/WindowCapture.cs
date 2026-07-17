using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Medoz.GameCommentary;

/// <summary>
/// Win32 API (EnumWindows + PrintWindow) を使い、ウィンドウタイトルの部分一致で
/// 対象ウィンドウを特定してキャプチャする (Python版 game_commentary.py の
/// find_target_window / capture_window_image 相当)。Windows 専用。
/// 見つからない場合は可視ウィンドウのタイトル一覧付きの例外を投げる
/// (AudioPlayer のデバイス未発見時と同じ UX)。
///
/// 制約: PrintWindow は対象ウィンドウへ WM_PRINT を送る方式のため、
/// **対象が自プロセスより高い整合性レベル(管理者権限)で動いている場合、UIPI に阻まれて
/// ERROR_ACCESS_DENIED(5) で失敗する**。管理者権限で動くゲームを撮るには
/// <see cref="WgcWindowCapture"/> (WGC) を使うこと (既定はそちら)。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowCapture : IWindowCapture
{
    private readonly IntPtr _hWnd;
    private readonly int _maxWidth;

    public string TargetTitle { get; }

    /// <summary>
    /// タイトルに <paramref name="titleFragment"/> を含む最初の可視ウィンドウを対象にする。
    /// 見つからなければ <see cref="InvalidOperationException"/>。
    /// </summary>
    public WindowCapture(string titleFragment, int maxWidth = 800)
    {
        _maxWidth = maxWidth;
        (_hWnd, TargetTitle) = WindowFinder.Find(titleFragment);
    }

    /// <summary>可視で空でないタイトルを持つウィンドウの一覧を返す。</summary>
    public static IReadOnlyList<string> GetVisibleWindowTitles()
        => WindowFinder.GetVisibleWindowTitles();

    /// <inheritdoc />
    public byte[] CaptureJpeg()
    {
        if (!GetWindowRect(_hWnd, out RECT rect))
        {
            throw new InvalidOperationException("対象ウィンドウの矩形取得に失敗しました。");
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"対象ウィンドウのサイズが不正です ({width}x{height})。最小化されていませんか?");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT(0x2): DWM 合成のモダンアプリでも中身を取得する
                if (!PrintWindow(_hWnd, hdc, PW_RENDERFULLCONTENT))
                {
                    throw new InvalidOperationException(BuildPrintWindowError(Marshal.GetLastWin32Error()));
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        (int targetWidth, int targetHeight) = ImageScaling.FitWidth(width, height, _maxWidth);
        if (targetWidth == width && targetHeight == height)
        {
            return EncodeJpeg(bitmap);
        }

        using var resized = new Bitmap(bitmap, targetWidth, targetHeight);
        return EncodeJpeg(resized);
    }

    /// <summary>
    /// PrintWindow 失敗時のメッセージ。Win32 エラーコードを含め、原因が特定しやすい
    /// ERROR_ACCESS_DENIED(5) には対処方法まで書く (原因不明の「失敗しました」を残さない)。
    /// </summary>
    internal static string BuildPrintWindowError(int win32Error)
    {
        const int ERROR_ACCESS_DENIED = 5;
        string message = $"PrintWindow によるキャプチャに失敗しました (Win32 error {win32Error})。";
        if (win32Error == ERROR_ACCESS_DENIED)
        {
            message += "\n対象ウィンドウが管理者権限で動いている可能性があります " +
                       "(この方式は自プロセスより高い整合性レベルのウィンドウを撮れません)。\n" +
                       $"CAPTURE_METHOD={WindowCaptureFactory.Wgc} (既定) にすればこの制約はありません。";
        }
        return message;
    }

    /// <summary>JPEG 品質80でエンコードしてバイト列を返す (Python版: quality=80)。</summary>
    private static byte[] EncodeJpeg(Bitmap bitmap)
    {
        ImageCodecInfo jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);

        using var ms = new MemoryStream();
        bitmap.Save(ms, jpegCodec, parameters);
        return ms.ToArray();
    }

    // ==== Win32 P/Invoke ====

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // SetLastError: 失敗理由 (特に ERROR_ACCESS_DENIED) をメッセージに出すため必須
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
