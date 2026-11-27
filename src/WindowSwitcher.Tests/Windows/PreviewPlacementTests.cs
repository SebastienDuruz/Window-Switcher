using System;
using Avalonia;
using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class PreviewPlacementTests
{
    private static readonly PixelRect WorkingArea = new(0, 0, 1920, 1040);
    private static readonly PixelSize PreviewSize = new(200, 100);

    [Fact]
    public void NextTo_PlacesThePreviewOnTheRightTopAligned()
    {
        var anchor = new PixelRect(100, 200, 300, 400);

        PixelPoint position = PreviewPlacement.NextTo(anchor, PreviewSize, WorkingArea, gap: 8);

        Assert.Equal(new PixelPoint(408, 200), position);
    }

    [Fact]
    public void NextTo_UsesTheLeftSideWhenTheRightSideIsTooNarrow()
    {
        var anchor = new PixelRect(1700, 200, 200, 400);

        PixelPoint position = PreviewPlacement.NextTo(anchor, PreviewSize, WorkingArea, gap: 8);

        Assert.Equal(new PixelPoint(1492, 200), position);
    }

    [Fact]
    public void NextTo_KeepsThePreviewInsideTheWorkingArea()
    {
        // Anchor wider than the screen sides allow and near the bottom edge.
        var anchor = new PixelRect(50, 1000, 1850, 40);

        PixelPoint position = PreviewPlacement.NextTo(anchor, PreviewSize, WorkingArea, gap: 8);

        Assert.Equal(new PixelPoint(0, 940), position);
    }

    [Fact]
    public void NextTo_UsesTheAreaOriginOfASecondaryScreen()
    {
        var secondScreen = new PixelRect(1920, -200, 1280, 1024);
        var anchor = new PixelRect(1900, -500, 300, 400);

        PixelPoint position = PreviewPlacement.NextTo(anchor, PreviewSize, secondScreen, gap: 8);

        Assert.Equal(new PixelPoint(2208, -200), position);
    }

    [Fact]
    public void NextTo_RejectsANegativeGap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewPlacement.NextTo(new PixelRect(0, 0, 10, 10), PreviewSize, WorkingArea, -1)
        );
    }
}
