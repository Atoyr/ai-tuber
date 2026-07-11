namespace Medoz.GameCommentary;

/// <summary>
/// 画像リサイズの寸法計算 (Python版 game_commentary.py の capture_window_image 内リサイズ相当)。
/// 実際の描画(System.Drawing)から純粋な計算を切り出してユニットテストしやすくする。
/// </summary>
public static class ImageScaling
{
    /// <summary>
    /// 幅が maxWidth を超える場合のみ、アスペクト比を保ったまま幅 maxWidth に縮小した寸法を返す。
    /// maxWidth 以下ならそのまま返す (Python版: img.width &gt; MAX_IMAGE_WIDTH のときだけ resize)。
    /// </summary>
    public static (int Width, int Height) FitWidth(int srcWidth, int srcHeight, int maxWidth)
    {
        if (srcWidth <= 0 || srcHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(srcWidth), "元画像の寸法は正の値である必要があります。");
        }
        if (maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "最大幅は正の値である必要があります。");
        }

        if (srcWidth <= maxWidth)
        {
            return (srcWidth, srcHeight);
        }

        // Python版: ratio = MAX_IMAGE_WIDTH / img.width; (MAX, int(img.height * ratio))
        double ratio = (double)maxWidth / srcWidth;
        int height = (int)(srcHeight * ratio);
        return (maxWidth, Math.Max(1, height));
    }
}
