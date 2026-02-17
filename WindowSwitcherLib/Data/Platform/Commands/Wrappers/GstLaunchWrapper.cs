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
    private const int OptimizedPipeWireMaxFps = 30;
    private const int OptimizedPipeWireJpegQuality = 70;
    private const int OptimizedQueueBufferCount = 1;

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

        Process? optimized = TryStartPipeWireJpegStream(nodeId, pipeWireRemoteFd, useVideoRate: true);
        if (optimized is null)
            return null;

        // If the optimized pipeline exits right away (e.g. missing videorate), retry with a compatible pipeline.
        if (!optimized.WaitForExit(milliseconds: 150))
            return optimized;

        optimized.Dispose();
        return TryStartPipeWireJpegStream(nodeId, pipeWireRemoteFd, useVideoRate: false);
    }

    private Process? TryStartPipeWireJpegStream(string nodeId, int? pipeWireRemoteFd, bool useVideoRate)
    {
        var process = CreateProcess();
        ConfigurePipeWireJpegPipeline(process.StartInfo, nodeId, pipeWireRemoteFd, useVideoRate);

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

    private static void ConfigurePipeWireJpegPipeline(
        ProcessStartInfo startInfo,
        string nodeId,
        int? pipeWireRemoteFd,
        bool useVideoRate)
    {
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("pipewiresrc");
        startInfo.ArgumentList.Add($"path={nodeId}");
        if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
            startInfo.ArgumentList.Add($"fd={pipeWireRemoteFd.Value}");
        startInfo.ArgumentList.Add("always-copy=true");
        startInfo.ArgumentList.Add("do-timestamp=true");
        startInfo.ArgumentList.Add("!");
        if (useVideoRate)
        {
            startInfo.ArgumentList.Add("videorate");
            startInfo.ArgumentList.Add("drop-only=true");
            startInfo.ArgumentList.Add($"max-rate={OptimizedPipeWireMaxFps}");
            startInfo.ArgumentList.Add("!");
        }

        startInfo.ArgumentList.Add("videoconvert");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("queue");
        startInfo.ArgumentList.Add("leaky=downstream");
        startInfo.ArgumentList.Add($"max-size-buffers={OptimizedQueueBufferCount}");
        startInfo.ArgumentList.Add("max-size-bytes=0");
        startInfo.ArgumentList.Add("max-size-time=0");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("jpegenc");
        startInfo.ArgumentList.Add($"quality={OptimizedPipeWireJpegQuality}");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("fdsink");
        startInfo.ArgumentList.Add("fd=1");
        startInfo.ArgumentList.Add("sync=false");
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
