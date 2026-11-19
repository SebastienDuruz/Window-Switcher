using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class X11RenderScalePlanTests
{
    [Fact]
    public void ComputeStages_HalvesThenReachesTarget()
    {
        IReadOnlyList<(int Width, int Height)> stages = X11RenderScalePlan.ComputeStages(
            1920,
            1080,
            400,
            225
        );

        Assert.Equal([(960, 540), (480, 270), (400, 225)], stages);
    }

    [Fact]
    public void ComputeStages_UsesSingleStage_WhenWithinTwiceTheTarget()
    {
        Assert.Equal([(600, 300)], X11RenderScalePlan.ComputeStages(800, 400, 600, 300));
    }

    [Fact]
    public void ComputeStages_DoesNotDuplicateTarget_WhenHalvingLandsOnIt()
    {
        Assert.Equal(
            [(800, 400), (400, 200)],
            X11RenderScalePlan.ComputeStages(1600, 800, 400, 200)
        );
    }

    [Theory]
    [InlineData(300, 200, 400, 225)]
    [InlineData(400, 225, 400, 225)]
    [InlineData(400, 100, 300, 200)]
    [InlineData(0, 100, 50, 50)]
    public void ComputeStages_ReturnsNoStage_WhenTargetIsNotSmaller(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight
    )
    {
        Assert.Empty(
            X11RenderScalePlan.ComputeStages(sourceWidth, sourceHeight, targetWidth, targetHeight)
        );
    }

    [Fact]
    public void ComputeStages_ClampsEachAxisToTarget_ForVeryDifferentRatios()
    {
        IReadOnlyList<(int Width, int Height)> stages = X11RenderScalePlan.ComputeStages(
            4000,
            300,
            250,
            200
        );

        Assert.Equal([(2000, 200), (1000, 200), (500, 200), (250, 200)], stages);
    }

    [Theory]
    [InlineData(1920, 1080, 400, 225, true, true)]
    [InlineData(1920, 1080, 400, 225, false, false)]
    [InlineData(400, 225, 400, 225, true, false)]
    [InlineData(300, 200, 400, 225, true, false)]
    [InlineData(400, 100, 300, 200, true, false)]
    [InlineData(400, 225, 400, 224, true, true)]
    public void ShouldScaleOnServer_ChoosesRenderOnlyForStrictlySmallerTarget(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight,
        bool renderAvailable,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            X11RenderScalePlan.ShouldScaleOnServer(
                sourceWidth,
                sourceHeight,
                targetWidth,
                targetHeight,
                renderAvailable
            )
        );
    }

    [Fact]
    public void CreateScaleTransform_MapsDestinationToSourceInFixedPoint()
    {
        XTransform transform = X11RenderScalePlan.CreateScaleTransform(1920, 1080, 960, 360);

        Assert.Equal(2 << 16, transform.Matrix11);
        Assert.Equal(3 << 16, transform.Matrix22);
        Assert.Equal(1 << 16, transform.Matrix33);
        Assert.Equal(0, transform.Matrix12);
        Assert.Equal(0, transform.Matrix13);
        Assert.Equal(0, transform.Matrix21);
        Assert.Equal(0, transform.Matrix23);
        Assert.Equal(0, transform.Matrix31);
        Assert.Equal(0, transform.Matrix32);
    }
}
