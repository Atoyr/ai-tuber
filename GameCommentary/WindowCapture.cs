using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Medoz.GameCommentary;

/// <summary>
/// Win32 API (EnumWindows + PrintWindow) を使い、ウィンドウタイトルの部分一致で
/// 対象ウィンドウを特定してキャプチャする (Python版 game_commentary.py の
/// find_target_window / capture_window_image 相当)。Windows 専用。
/// 見つからない場合は可視ウィンドウのタイトル一覧付きの例外を投げる
/// (AudioPlayer のデバイス未発見時と同じ UX)。
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
        if (string.IsNullOrEmpty(titleFragment))
        {
            throw new ArgumentNullException(nameof(titleFragment), "ウィンドウタイトルの一部を指定してください。");
        }
        _maxWidth = maxWidth;

        var windows = EnumerateVisibleWindows();
        var match = windows.FirstOrDefault(w => w.Title.Contains(titleFragment, StringComparison.Ordinal));
        if (match.Handle == IntPtr.Zero)
        {
            string titles = string.Join(", ", windows.Select(w => $"'{w.Title}'"));
            throw new InvalidOperationException(
                $"'{titleFragment}' を含むウィンドウが見つかりません。\n" +
                $"現在開いているウィンドウ一覧: [{titles}]\n" +
                $"ウィンドウタイトルの一部をこの中の文字列に書き換えてください。");
        }
        _hWnd = match.Handle;
        TargetTitle = match.Title;
    }

    /// <summary>可視で空でないタイトルを持つウィンドウの一覧を返す。</summary>
    public static IReadOnlyList<string> GetVisibleWindowTitles()
        => EnumerateVisibleWindows().Select(w => w.Title).ToList();

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
                    throw new InvalidOperationException("PrintWindow によるキャプチャに失敗しました。");
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

    private static List<(IntPtr Handle, string Title)> EnumerateVisibleWindows()
    {
        var result = new List<(IntPtr, string)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }
            int length = GetWindowTextLength(hWnd);
            if (length <= 0)
            {
                return true;
            }
            var sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (!string.IsNullOrEmpty(title))
            {
                result.Add((hWnd, title));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // ==== Win32 P/Invoke ====

    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
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
