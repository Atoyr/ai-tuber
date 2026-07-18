using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

/// <summary>
/// PrintWindow 失敗時のメッセージ。実際にキャプチャを走らせずメッセージ組み立てだけを検証する
/// (原因不明の「失敗しました」に戻さないための回帰テスト)。
/// </summary>
public class WindowCaptureErrorTests
{
    [Fact]
    public void BuildPrintWindowError_AccessDenied_ExplainsElevationAndWayOut()
    {
        // ERROR_ACCESS_DENIED(5) = 対象が自プロセスより高い整合性レベル (管理者権限で動くゲーム等)
        string message = WindowCapture.BuildPrintWindowError(5);

        Assert.Contains("5", message);
        Assert.Contains("管理者権限", message);
        Assert.Contains(WindowCaptureFactory.Wgc, message); // 対処方法まで書いてあること
    }

    [Fact]
    public void BuildPrintWindowError_OtherError_IncludesCodeWithoutElevationHint()
    {
        // 権限以外の失敗で「管理者権限が原因」と誤誘導しないこと
        string message = WindowCapture.BuildPrintWindowError(87);

        Assert.Contains("87", message);
        Assert.DoesNotContain("管理者権限", message);
    }
}
