using System.Diagnostics;
using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class GstLaunchWrapper() : CommandBase("gst-launch-1.0"), IGstLaunchWrapper
{
    public string Execute(string args)
    {
        if (LinuxDependencies.IsGstLaunchAvailable) return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
        return string.Empty;

    }

    public Process? StartPipeWireJpegStream(string nodeId, int fps)
    {
        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            return null;
        }

        var process = CreateProcess();
        process.StartInfo.ArgumentList.Add("-q");
        process.StartInfo.ArgumentList.Add("pipewiresrc");
        process.StartInfo.ArgumentList.Add($"path={nodeId}");
        process.StartInfo.ArgumentList.Add("do-timestamp=true");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add("videorate");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add($"video/x-raw,framerate={fps}/1");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add("videoconvert");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add("queue");
        process.StartInfo.ArgumentList.Add("leaky=downstream");
        process.StartInfo.ArgumentList.Add("max-size-buffers=2");
        process.StartInfo.ArgumentList.Add("max-size-bytes=0");
        process.StartInfo.ArgumentList.Add("max-size-time=0");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add("jpegenc");
        process.StartInfo.ArgumentList.Add("quality=80");
        process.StartInfo.ArgumentList.Add("!");
        process.StartInfo.ArgumentList.Add("fdsink");
        process.StartInfo.ArgumentList.Add("fd=1");
        process.StartInfo.ArgumentList.Add("sync=false");

        try
        {
            process.Start();
            return process;
        }
        catch
        {
            process.Dispose();
            return null;
        }
    }
}
