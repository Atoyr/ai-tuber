using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

public class WindowCaptureFactoryTests
{
    [Fact]
    public void NormalizeMethod_DefaultsToWgc_WhenUnset()
    {
        // 既定は WGC (管理者権限のウィンドウ・隠れたウィンドウも撮れる方式)
        Assert.Equal(WindowCaptureFactory.Wgc, WindowCaptureFactory.NormalizeMethod(null));
        Assert.Equal(WindowCaptureFactory.Wgc, WindowCaptureFactory.NormalizeMethod(""));
        Assert.Equal(WindowCaptureFactory.Wgc, WindowCaptureFactory.NormalizeMethod("   "));
    }

    [Theory]
    [InlineData("wgc", "wgc")]
    [InlineData("WGC", "wgc")]
    [InlineData("  Wgc  ", "wgc")]
    [InlineData("printwindow", "printwindow")]
    [InlineData("PrintWindow", "printwindow")]
    public void NormalizeMethod_IsCaseAndSpaceInsensitive(string input, string expected)
    {
        Assert.Equal(expected, WindowCaptureFactory.NormalizeMethod(input));
    }

    [Fact]
    public void NormalizeMethod_UnknownMethod_ThrowsListingValidValues()
    {
        var ex = Assert.Throws<ArgumentException>(() => WindowCaptureFactory.NormalizeMethod("bitblt"));

        // 何が悪くて何なら通るのかがメッセージだけで分かること
        Assert.Contains("bitblt", ex.Message);
        Assert.Contains(WindowCaptureFactory.Wgc, ex.Message);
        Assert.Contains(WindowCaptureFactory.PrintWindow, ex.Message);
    }
}
