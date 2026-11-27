using System;
using Avalonia;
using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class PreviewSlotLayoutTests
{
    // 1000 x 500 area at (100, 50): with 16 px margins, 200 x 100 previews and 8 px gaps,
    // 4 columns and 4 rows fit.
    private static readonly PixelRect WorkingArea = new(100, 50, 1000, 500);
    private static readonly PixelSize PreviewSize = new(200, 100);

    [Theory]
    [InlineData(0, 116, 66)]
    [InlineData(1, 324, 66)]
    [InlineData(3, 740, 66)]
    [InlineData(4, 116, 174)]
    [InlineData(15, 740, 390)]
    public void GetPosition_FillsRowsFromTheTopLeftCorner(int slot, int x, int y)
    {
        PixelPoint position = PreviewSlotLayout.GetPosition(
            slot,
            WorkingArea,
            PreviewSize,
            margin: 16,
            gap: 8
        );

        Assert.Equal(new PixelPoint(x, y), position);
    }

    [Fact]
    public void GetPosition_StartsOverWhenTheAreaIsFull()
    {
        PixelPoint first = PreviewSlotLayout.GetPosition(0, WorkingArea, PreviewSize, 16, 8);
        PixelPoint wrapped = PreviewSlotLayout.GetPosition(16, WorkingArea, PreviewSize, 16, 8);

        Assert.Equal(first, wrapped);
    }

    [Fact]
    public void GetPosition_KeepsOneSlotWhenThePreviewIsLargerThanTheArea()
    {
        var tinyArea = new PixelRect(0, 0, 50, 50);

        PixelPoint position = PreviewSlotLayout.GetPosition(3, tinyArea, PreviewSize, 16, 8);

        Assert.Equal(new PixelPoint(16, 16), position);
    }

    [Fact]
    public void GetPosition_RejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewSlotLayout.GetPosition(-1, WorkingArea, PreviewSize, 16, 8)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewSlotLayout.GetPosition(0, WorkingArea, PreviewSize, -1, 8)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PreviewSlotLayout.GetPosition(0, WorkingArea, PreviewSize, 16, -1)
        );
    }
}

public sealed class PreviewSlotAllocatorTests
{
    [Fact]
    public void Acquire_GivesEachWindowTheLowestFreeSlot()
    {
        var sut = new PreviewSlotAllocator();

        Assert.Equal(0, sut.Acquire("a"));
        Assert.Equal(1, sut.Acquire("b"));
        Assert.Equal(2, sut.Acquire("c"));
    }

    [Fact]
    public void Acquire_ReturnsTheSameSlotForTheSameWindow()
    {
        var sut = new PreviewSlotAllocator();
        _ = sut.Acquire("a");

        Assert.Equal(0, sut.Acquire("a"));
        Assert.Equal(1, sut.Acquire("b"));
    }

    [Fact]
    public void Release_FreesTheSlotForTheNextWindow()
    {
        var sut = new PreviewSlotAllocator();
        _ = sut.Acquire("a");
        _ = sut.Acquire("b");
        _ = sut.Acquire("c");

        sut.Release("b");

        Assert.Equal(1, sut.Acquire("d"));
        Assert.Equal(3, sut.Acquire("e"));
    }

    [Fact]
    public void Clear_FreesEverySlot()
    {
        var sut = new PreviewSlotAllocator();
        _ = sut.Acquire("a");
        _ = sut.Acquire("b");

        sut.Clear();

        Assert.Equal(0, sut.Acquire("c"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Release_IgnoresAnEmptyWindowId(string? windowId)
    {
        var sut = new PreviewSlotAllocator();
        _ = sut.Acquire("a");

        sut.Release(windowId!);

        Assert.Equal(1, sut.Acquire("b"));
    }
}
