using System;
using Avalonia;

namespace WindowSwitcher.Windows.Services;

/// <summary>
/// Places previews that have no saved position side by side, row by row, inside the
/// working area of a screen, so they do not open on top of each other.
/// </summary>
internal static class PreviewSlotLayout
{
    /// <summary>
    /// Returns the top-left corner of <paramref name="slot"/>. Slots fill the working
    /// area from its top-left corner, left to right then top to bottom; when the area is
    /// full, slots start over from the first position.
    /// </summary>
    /// <param name="slot">Zero-based slot index.</param>
    /// <param name="workingArea">Usable screen area, in physical pixels.</param>
    /// <param name="previewSize">Preview size, in physical pixels.</param>
    /// <param name="margin">Space kept between the area edges and the previews.</param>
    /// <param name="gap">Space kept between two previews.</param>
    public static PixelPoint GetPosition(
        int slot,
        PixelRect workingArea,
        PixelSize previewSize,
        int margin,
        int gap
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        ArgumentOutOfRangeException.ThrowIfNegative(gap);

        int cellWidth = Math.Max(1, previewSize.Width) + gap;
        int cellHeight = Math.Max(1, previewSize.Height) + gap;
        int columns = Math.Max(1, (workingArea.Width - 2 * margin + gap) / cellWidth);
        int rows = Math.Max(1, (workingArea.Height - 2 * margin + gap) / cellHeight);

        int wrappedSlot = slot % (columns * rows);
        int column = wrappedSlot % columns;
        int row = wrappedSlot / columns;

        return new PixelPoint(
            workingArea.X + margin + column * cellWidth,
            workingArea.Y + margin + row * cellHeight
        );
    }
}
