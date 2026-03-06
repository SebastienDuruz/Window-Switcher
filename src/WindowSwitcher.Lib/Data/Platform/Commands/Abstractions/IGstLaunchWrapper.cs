using System.Diagnostics;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface IGstLaunchWrapper : ICommandWrapper
{
    Process? StartPipeWireRawBgraStream(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd = null
    );
}
