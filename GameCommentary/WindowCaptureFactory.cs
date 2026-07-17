using System.Runtime.Versioning;

namespace Medoz.GameCommentary;

/// <summary>
/// キャプチャ方式を選んで <see cref="IWindowCapture"/> を作る
/// (OBS のウィンドウキャプチャにある「キャプチャ方法」の選択に相当)。
/// 方式名の正規化・検証は副作用の無い static メソッドに分離してユニットテスト対象にする
/// (プラットフォーム属性は実際に WGC に触れる <see cref="Create"/> にだけ付ける。
///  定数と <see cref="NormalizeMethod"/> はただの文字列処理なので呼び出し側を縛らない)。
/// </summary>
public static class WindowCaptureFactory
{
    /// <summary>Windows.Graphics.Capture (既定)。OBS の「Windows 10 (1903以降)」と同じ方式。</summary>
    public const string Wgc = "wgc";

    /// <summary>従来の PrintWindow 方式。管理者権限のウィンドウは撮れない。</summary>
    public const string PrintWindow = "printwindow";

    /// <summary>指定方式で対象ウィンドウのキャプチャを作る。</summary>
    /// <param name="method">"wgc" | "printwindow" (大文字小文字・前後空白は無視)</param>
    /// <exception cref="ArgumentException">未知の方式。</exception>
    /// <exception cref="InvalidOperationException">対象ウィンドウが見つからない / WGC 非対応環境。</exception>
    [SupportedOSPlatform("windows10.0.19041")]
    public static IWindowCapture Create(string method, string titleFragment, int maxWidth = 800)
        => NormalizeMethod(method) switch
        {
            Wgc => new WgcWindowCapture(titleFragment, maxWidth),
            PrintWindow => new WindowCapture(titleFragment, maxWidth),
            var normalized => throw new InvalidOperationException($"到達しないはず: {normalized}"),
        };

    /// <summary>
    /// 方式名を正規化する。空なら既定の <see cref="Wgc"/>。未知の値は有効値を示す例外。
    /// </summary>
    public static string NormalizeMethod(string? method)
    {
        string normalized = (method ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return Wgc;
        }
        return normalized switch
        {
            Wgc => Wgc,
            PrintWindow => PrintWindow,
            _ => throw new ArgumentException(
                $"未知のキャプチャ方式です: '{method}' ({Wgc} / {PrintWindow} のいずれかを指定してください)",
                nameof(method)),
        };
    }
}
