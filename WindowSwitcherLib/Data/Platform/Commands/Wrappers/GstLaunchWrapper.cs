using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcherLib.Data.Platform.Commands.Wrappers;

public sealed class GstLaunchWrapper() : CommandBase("gst-launch-1.0"), IGstLaunchWrapper
{
    private const int FGetFd = 1;
    private const int FSetFd = 2;
    private const int FdCloExec = 1;

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int cmd, int arg);

    public string Execute(string args)
    {
        if (LinuxDependencies.IsGstLaunchAvailable) return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
        return string.Empty;

    }

    public Process? StartPipeWireJpegStream(string nodeId, int? pipeWireRemoteFd = null)
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
        if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
            process.StartInfo.ArgumentList.Add($"fd={pipeWireRemoteFd.Value}");
        process.StartInfo.ArgumentList.Add("always-copy=true");
        process.StartInfo.ArgumentList.Add("do-timestamp=true");
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
            if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
                SetCloseOnExec(pipeWireRemoteFd.Value, enabled: false);

            process.Start();

            if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
                SetCloseOnExec(pipeWireRemoteFd.Value, enabled: true);

            return process;
        }
        catch
        {
            if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
                SetCloseOnExec(pipeWireRemoteFd.Value, enabled: true);

            process.Dispose();
            return null;
        }
    }

    private static void SetCloseOnExec(int fileDescriptor, bool enabled)
    {
        if (fileDescriptor < 0)
            return;

        int flags = Fcntl(fileDescriptor, FGetFd, 0);
        if (flags < 0)
            return;

        int updated = enabled ? (flags | FdCloExec) : (flags & ~FdCloExec);
        if (updated == flags)
            return;

        _ = Fcntl(fileDescriptor, FSetFd, updated);
    }
}
