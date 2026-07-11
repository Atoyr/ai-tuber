namespace Medoz.GameCommentary;

/// <summary>
/// 対象ウィンドウをキャプチャして JPEG バイト列を返す抽象。
/// Win32 依存の実装 (<see cref="WindowCapture"/>) をメインループから切り離し、
/// ループ本体 (<see cref="CommentaryLoop"/>) をフェイク注入でテストできるようにする。
/// </summary>
public interface IWindowCapture
{
    /// <summary>対象ウィンドウのタイトル(部分一致に使った文字列など。ログ用)。</summary>
    string TargetTitle { get; }

    /// <summary>今のウィンドウ内容をキャプチャし、リサイズ済み JPEG バイト列で返す。</summary>
    byte[] CaptureJpeg();
}
