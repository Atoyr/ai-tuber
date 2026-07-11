using Medoz.GameCommentary;

namespace Medoz.GameCommentary.Tests;

public class ImageScalingTests
{
    [Fact]
    public void FitWidth_ScalesDown_KeepingAspectRatio()
    {
        // 1600x900 を幅800へ: 高さは 900 * (800/1600) = 450
        (int width, int height) = ImageScaling.FitWidth(1600, 900, 800);
        Assert.Equal(800, width);
        Assert.Equal(450, height);
    }

    [Fact]
    public void FitWidth_LeavesSmallImageUnchanged()
    {
        // Python版: img.width > MAX のときだけ resize
        (int width, int height) = ImageScaling.FitWidth(600, 400, 800);
        Assert.Equal(600, width);
        Assert.Equal(400, height);
    }

    [Fact]
    public void FitWidth_AtExactMaxWidth_Unchanged()
    {
        (int width, int height) = ImageScaling.FitWidth(800, 600, 800);
        Assert.Equal(800, width);
        Assert.Equal(600, height);
    }

    [Fact]
    public void FitWidth_TruncatesHeight_LikePythonInt()
    {
        // 1000x333, 幅500: 333 * 0.5 = 166.5 → int で 166 (切り捨て)
        (int width, int height) = ImageScaling.FitWidth(1000, 333, 500);
        Assert.Equal(500, width);
        Assert.Equal(166, height);
    }

    [Fact]
    public void FitWidth_VeryWideImage_HeightAtLeastOne()
    {
        (int _, int height) = ImageScaling.FitWidth(10000, 3, 800);
        Assert.True(height >= 1);
    }

    [Theory]
    [InlineData(0, 100, 800)]
    [InlineData(100, 0, 800)]
    [InlineData(100, 100, 0)]
    public void FitWidth_Throws_OnNonPositive(int w, int h, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageScaling.FitWidth(w, h, max));
    }
}
