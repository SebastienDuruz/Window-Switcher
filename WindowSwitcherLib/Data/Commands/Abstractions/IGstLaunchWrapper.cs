using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public interface IGstLaunchWrapper : ICommandWrapper
{
    Process? StartPipeWireJpegStream(string nodeId, int fps);
}
