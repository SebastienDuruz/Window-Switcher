using System.Diagnostics;

namespace WindowSwitcherLib.Data.Platform.Commands.Abstractions;

public interface IGstLaunchWrapper : ICommandWrapper
{
    Process? StartPipeWireRawBgraStream(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd = null
    );

    Process? StartPipeWireJpegStream(
        string nodeId,
        int? pipeWireRemoteFd = null,
        int? maxWidthPx = null,
        int? maxHeightPx = null
    );
}
