using System.Diagnostics;

namespace WindowSwitcherLib.Data.Platform.Commands.Abstractions;

public interface IGstLaunchWrapper : ICommandWrapper
{
    Process? StartPipeWireJpegStream(string nodeId, int? pipeWireRemoteFd = null);
}
