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
}
