using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class GstLaunchWrapperTests
{
    [Fact]
    public void BuildPipeWirePipelineDescription_ThrottlesBeforeConvertingAndScaling()
    {
        string pipeline = GstLaunchWrapper.BuildPipeWirePipelineDescription(
            "42",
            300,
            188,
            pipeWireRemoteFd: 7,
            includeConversionPipeline: true
        );

        Assert.Contains(
            "pipewiresrc path=\"42\" fd=7 always-copy=false use-bufferpool=true do-timestamp=true ! videorate drop-only=true max-rate=20 ! videoconvert ! videoscale add-borders=false ! video/x-raw,format=BGRA,width=300,height=188",
            pipeline
        );
    }

    [Fact]
    public void BuildPipeWirePipelineDescription_DoesNotAddThrottleWithoutConversion()
    {
        string pipeline = GstLaunchWrapper.BuildPipeWirePipelineDescription(
            "42",
            300,
            188,
            pipeWireRemoteFd: null,
            includeConversionPipeline: false
        );

        Assert.DoesNotContain("videorate", pipeline);
    }
}
