using System;
using Avalonia;

namespace WindowSwitcher.Windows.Services;

/// <summary>
/// Places a preview next to another window, so a lost preview can be brought back in view.
/// </summary>
internal static class PreviewPlacement
{
    /// <summary>
    /// Returns the top-left corner for a preview of <paramref name="previewSize"/> placed
    /// to the right of <paramref name="anchor"/>, top-aligned with it. When the preview
    /// does not fit on the right, it goes to the left; the result always stays inside
    /// <paramref name="workingArea"/>.
    /// </summary>
    /// <param name="anchor">Bounds of the window to place the preview next to, in physical pixels.</param>
    /// <param name="previewSize">Preview size, in physical pixels.</param>
    /// <param name="workingArea">Usable area of the screen showing the anchor, in physical pixels.</param>
    /// <param name="gap">Space kept between the anchor and the preview.</param>
    public static PixelPoint NextTo(
        PixelRect anchor,
        PixelSize previewSize,
        PixelRect workingArea,
        int gap
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        int x = anchor.Right + gap;
        if (x + previewSize.Width > workingArea.Right)
            x = anchor.X - gap - previewSize.Width;

        return new PixelPoint(
            Clamp(x, workingArea.X, workingArea.Right - previewSize.Width),
            Clamp(anchor.Y, workingArea.Y, workingArea.Bottom - previewSize.Height)
        );
    }

    // Keeps the preview inside the area; a preview larger than the area sticks to its
    // top-left corner.
    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(value, Math.Max(minimum, maximum)));
    }
}
