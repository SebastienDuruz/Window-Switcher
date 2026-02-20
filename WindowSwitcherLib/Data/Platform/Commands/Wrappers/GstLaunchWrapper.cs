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
    private const int PipeWireMaxFps = 30;
    private const int QueueBufferCount = 1;

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int cmd, int arg);

    public string Execute(string args)
    {
        if (LinuxDependencies.IsGstLaunchAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
        return string.Empty;
    }

    public Process? StartPipeWireRawBgraStream(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd = null
    )
    {
        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            return null;
        }

        int? normalizedWidthPx = NormalizePipeWireDimension(widthPx);
        int? normalizedHeightPx = NormalizePipeWireDimension(heightPx);
        if (!normalizedWidthPx.HasValue || !normalizedHeightPx.HasValue)
            return null;

        Process? process = TryStartPipeWireRawBgraStream(
            nodeId,
            pipeWireRemoteFd,
            useVideoRate: true,
            widthPx: normalizedWidthPx.Value,
            heightPx: normalizedHeightPx.Value
        );
        if (process is null)
            return null;

        if (!process.WaitForExit(milliseconds: 150))
            return process;

        process.Dispose();
        return null;
    }

    private Process? TryStartPipeWireRawBgraStream(
        string nodeId,
        int? pipeWireRemoteFd,
        bool useVideoRate,
        int widthPx,
        int heightPx
    )
    {
        var process = CreateProcess();
        ConfigurePipeWireRawBgraPipeline(
            process.StartInfo,
            nodeId,
            pipeWireRemoteFd,
            useVideoRate,
            widthPx,
            heightPx
        );

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

    private static void ConfigurePipeWireRawBgraPipeline(
        ProcessStartInfo startInfo,
        string nodeId,
        int? pipeWireRemoteFd,
        bool useVideoRate,
        int widthPx,
        int heightPx
    )
    {
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add("-q");
        startInfo.ArgumentList.Add("pipewiresrc");
        startInfo.ArgumentList.Add($"path={nodeId}");
        if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
            startInfo.ArgumentList.Add($"fd={pipeWireRemoteFd.Value}");
        startInfo.ArgumentList.Add("always-copy=true");
        startInfo.ArgumentList.Add("use-bufferpool=false");
        startInfo.ArgumentList.Add("do-timestamp=true");
        startInfo.ArgumentList.Add("!");
        if (useVideoRate)
        {
            startInfo.ArgumentList.Add("videorate");
            startInfo.ArgumentList.Add("drop-only=true");
            startInfo.ArgumentList.Add($"max-rate={PipeWireMaxFps}");
            startInfo.ArgumentList.Add("!");
        }

        startInfo.ArgumentList.Add("videoconvert");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("videoscale");
        startInfo.ArgumentList.Add("add-borders=true");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add(
            $"video/x-raw,format=BGRA,width={widthPx},height={heightPx},pixel-aspect-ratio=1/1"
        );
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("queue");
        startInfo.ArgumentList.Add("leaky=downstream");
        startInfo.ArgumentList.Add($"max-size-buffers={QueueBufferCount}");
        startInfo.ArgumentList.Add("max-size-bytes=0");
        startInfo.ArgumentList.Add("max-size-time=0");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("fdsink");
        startInfo.ArgumentList.Add("fd=1");
        startInfo.ArgumentList.Add("sync=false");
    }

    private static int? NormalizePipeWireDimension(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return null;

        return value.Value;
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
