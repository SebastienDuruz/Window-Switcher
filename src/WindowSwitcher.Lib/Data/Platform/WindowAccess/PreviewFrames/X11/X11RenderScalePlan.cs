namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

/// <summary>
/// Pure planning helpers for the XRender downscale chain: decides whether the X server should
/// shrink the window pixmap and computes the intermediate sizes and transforms.
/// </summary>
internal static class X11RenderScalePlan
{
    private const int FixedOne = 1 << 16;

    /// <summary>
    /// Tells whether the readback should go through the XRender downscale chain.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when XRender is available and the target is strictly smaller than
    /// the source without being larger on either axis.
    /// </returns>
    public static bool ShouldScaleOnServer(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool renderAvailable
    ) =>
        renderAvailable
        && sourceWidth > 0
        && sourceHeight > 0
        && targetWidth > 0
        && targetHeight > 0
        && targetWidth <= sourceWidth
        && targetHeight <= sourceHeight
        && (targetWidth < sourceWidth || targetHeight < sourceHeight);

    /// <summary>
    /// Computes the successive sizes of the downscale chain. Each stage halves the previous
    /// size (never going below the target) while it is more than twice the target, then a last
    /// stage reaches the exact target size.
    /// </summary>
    /// <returns>The stage sizes, ending with the target; empty when no scaling is needed.</returns>
    public static IReadOnlyList<(int Width, int Height)> ComputeStages(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight
    )
    {
        if (!ShouldScaleOnServer(sourceWidth, sourceHeight, targetWidth, targetHeight, true))
            return [];

        var stages = new List<(int Width, int Height)>();
        int width = sourceWidth;
        int height = sourceHeight;
        while (width > 2L * targetWidth || height > 2L * targetHeight)
        {
            width = Math.Max(width / 2, targetWidth);
            height = Math.Max(height / 2, targetHeight);
            stages.Add((width, height));
        }

        if (width != targetWidth || height != targetHeight)
            stages.Add((targetWidth, targetHeight));

        return stages;
    }

    /// <summary>
    /// Builds the XRender transform that maps a destination of the given size onto a source of
    /// the given size (pure scaling, no translation).
    /// </summary>
    public static XTransform CreateScaleTransform(
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationHeight);

        return new XTransform
        {
            Matrix11 = ToFixedRatio(sourceWidth, destinationWidth),
            Matrix22 = ToFixedRatio(sourceHeight, destinationHeight),
            Matrix33 = FixedOne,
        };
    }

    private static int ToFixedRatio(int numerator, int denominator) =>
        checked((int)((long)numerator * FixedOne / denominator));
}
