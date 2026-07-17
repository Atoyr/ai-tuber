using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Medoz.GameCommentary;

/// <summary>
/// ウィンドウタイトルの部分一致で対象ウィンドウ(HWND)を特定する共通処理。
/// キャプチャ方式 (<see cref="WindowCapture"/> = PrintWindow / <see cref="WgcWindowCapture"/> = WGC)
/// によらず、探索と「見つからない場合は可視ウィンドウ一覧付きの例外」の UX を同一にするため
/// ここに集約する (AudioPlayer のデバイス未発見時と同じ方針)。
/// 探索・メッセージ組み立ての純粋部分は static メソッドに分離してユニットテスト対象にする。
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowFinder
{
    /// <summary>可視で空でないタイトルを持つウィンドウの一覧を返す。</summary>
    public static IReadOnlyList<string> GetVisibleWindowTitles()
        => EnumerateVisibleWindows().Select(w => w.Title).ToList();

    /// <summary>
    /// タイトルに <paramref name="titleFragment"/> を含む最初の可視ウィンドウを返す。
    /// 見つからなければ候補一覧付きの <see cref="InvalidOperationException"/>。
    /// </summary>
    public static (IntPtr Handle, string Title) Find(string titleFragment)
    {
        if (string.IsNullOrEmpty(titleFragment))
        {
            throw new ArgumentNullException(nameof(titleFragment), "ウィンドウタイトルの一部を指定してください。");
        }

        var windows = EnumerateVisibleWindows();
        int index = IndexOfMatch(windows.Select(w => w.Title).ToList(), titleFragment);
        if (index < 0)
        {
            throw new InvalidOperationException(
                BuildNotFoundMessage(titleFragment, windows.Select(w => w.Title).ToList()));
        }
        return windows[index];
    }

    /// <summary>
    /// <paramref name="titles"/> のうち <paramref name="fragment"/> を含む最初の要素の位置。
    /// 無ければ -1。
    /// </summary>
    internal static int IndexOfMatch(IReadOnlyList<string> titles, string fragment)
    {
        for (int i = 0; i < titles.Count; i++)
        {
            if (titles[i].Contains(fragment, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>対象ウィンドウが見つからなかったときのメッセージ (候補一覧を含める)。</summary>
    internal static string BuildNotFoundMessage(string fragment, IReadOnlyList<string> titles)
    {
        string list = string.Join(", ", titles.Select(t => $"'{t}'"));
        return $"'{fragment}' を含むウィンドウが見つかりません。\n" +
               $"現在開いているウィンドウ一覧: [{list}]\n" +
               $"ウィンドウタイトルの一部をこの中の文字列に書き換えてください。";
    }

    internal static List<(IntPtr Handle, string Title)> EnumerateVisibleWindows()
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

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
