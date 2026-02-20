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
    private const int PipeWireJpegQuality = 70;
    private const int QueueBufferCount = 1;
    private const int MaxScaledDimensionPx = 8192;

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int cmd, int arg);

    public string Execute(string args)
    {
        if (LinuxDependencies.IsGstLaunchAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
        return string.Empty;
    }

    public Process? StartPipeWireJpegStream(
        string nodeId,
        int? pipeWireRemoteFd = null,
        int? maxWidthPx = null,
        int? maxHeightPx = null
    )
    {
        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            return null;
        }

        int? normalizedMaxWidthPx = NormalizePipeWireDimension(maxWidthPx);
        int? normalizedMaxHeightPx = NormalizePipeWireDimension(maxHeightPx);

        Process? optimized = TryStartPipeWireJpegStream(
            nodeId,
            pipeWireRemoteFd,
            useVideoRate: true,
            maxWidthPx: normalizedMaxWidthPx,
            maxHeightPx: normalizedMaxHeightPx
        );
        if (optimized is null)
            return null;

        // If the optimized pipeline exits right away (e.g. missing videorate), retry with a compatible pipeline.
        if (!optimized.WaitForExit(milliseconds: 150))
            return optimized;

        optimized.Dispose();
        Process? compatible = TryStartPipeWireJpegStream(
            nodeId,
            pipeWireRemoteFd,
            useVideoRate: false,
            maxWidthPx: normalizedMaxWidthPx,
            maxHeightPx: normalizedMaxHeightPx
        );
        if (compatible is null)
            return null;

        if (!compatible.WaitForExit(milliseconds: 150))
            return compatible;

        compatible.Dispose();

        // Last-resort fallback when videoscale/caps negotiation fails on some environments.
        if (!normalizedMaxWidthPx.HasValue && !normalizedMaxHeightPx.HasValue)
            return null;

        return TryStartPipeWireJpegStream(
            nodeId,
            pipeWireRemoteFd,
            useVideoRate: false,
            maxWidthPx: null,
            maxHeightPx: null
        );
    }

    private Process? TryStartPipeWireJpegStream(
        string nodeId,
        int? pipeWireRemoteFd,
        bool useVideoRate,
        int? maxWidthPx,
        int? maxHeightPx
    )
    {
        var process = CreateProcess();
        ConfigurePipeWireJpegPipeline(
            process.StartInfo,
            nodeId,
            pipeWireRemoteFd,
            useVideoRate,
            maxWidthPx,
            maxHeightPx
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

    private static void ConfigurePipeWireJpegPipeline(
        ProcessStartInfo startInfo,
        string nodeId,
        int? pipeWireRemoteFd,
        bool useVideoRate,
        int? maxWidthPx,
        int? maxHeightPx
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
        if (maxWidthPx.HasValue || maxHeightPx.HasValue)
        {
            startInfo.ArgumentList.Add("videoscale");
            startInfo.ArgumentList.Add("add-borders=true");
            startInfo.ArgumentList.Add("!");
            startInfo.ArgumentList.Add(BuildScaledVideoCaps(maxWidthPx, maxHeightPx));
            startInfo.ArgumentList.Add("!");
        }

        startInfo.ArgumentList.Add("queue");
        startInfo.ArgumentList.Add("leaky=downstream");
        startInfo.ArgumentList.Add($"max-size-buffers={QueueBufferCount}");
        startInfo.ArgumentList.Add("max-size-bytes=0");
        startInfo.ArgumentList.Add("max-size-time=0");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("jpegenc");
        startInfo.ArgumentList.Add($"quality={PipeWireJpegQuality}");
        startInfo.ArgumentList.Add("!");
        startInfo.ArgumentList.Add("fdsink");
        startInfo.ArgumentList.Add("fd=1");
        startInfo.ArgumentList.Add("sync=false");
    }

    private static int? NormalizePipeWireDimension(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return null;

        return Math.Clamp(value.Value, 1, MaxScaledDimensionPx);
    }

    private static string BuildScaledVideoCaps(int? maxWidthPx, int? maxHeightPx)
    {
        string caps = "video/x-raw,pixel-aspect-ratio=1/1";
        if (maxWidthPx.HasValue)
            caps += $",width={maxWidthPx.Value}";
        if (maxHeightPx.HasValue)
            caps += $",height={maxHeightPx.Value}";

        return caps;
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
