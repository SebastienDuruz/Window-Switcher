using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public sealed class GstLaunchWrapper() : CommandBase("gst-launch-1.0"), IGstLaunchWrapper
{
    public string Execute(string args)
    {
        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            return string.Empty;
        }

        return ExecuteWithArguments(args, timeoutMs: 2_500);
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
