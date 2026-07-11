using System.Runtime.InteropServices;
using System.Text;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public sealed class GstLaunchWrapper() : CommandBase("gst-launch-1.0"), IGstLaunchWrapper
{
    private const int QueueBufferCount = 2;
    private const int MaximumPreviewFrameRate = 20;
    private const string SinkName = "ws_sink";

    public string Execute(string args)
    {
        if (LinuxDependencies.IsGstLaunchAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
        return string.Empty;
    }

    public IPipeWireRawBgraStream? StartPipeWireRawBgraStream(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd = null
    )
    {
        if (!LinuxDependencies.IsGstPipeWireSrcAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gstreamer pipewiresrc");
            return null;
        }

        int? normalizedWidthPx = NormalizePipeWireDimension(widthPx);
        int? normalizedHeightPx = NormalizePipeWireDimension(heightPx);
        if (!normalizedWidthPx.HasValue || !normalizedHeightPx.HasValue)
            return null;

        return GStreamerAppSinkPipeWireRawBgraStream.TryStart(
            nodeId,
            normalizedWidthPx.Value,
            normalizedHeightPx.Value,
            pipeWireRemoteFd
        );
    }

    private static int? NormalizePipeWireDimension(int? value)
    {
        if (!value.HasValue || value.Value <= 0)
            return null;

        return value.Value;
    }

    internal static string BuildPipeWirePipelineDescription(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd,
        bool includeConversionPipeline
    )
    {
        var builder = new StringBuilder();
        builder.Append("pipewiresrc ");
        builder.Append("path=");
        builder.Append(QuoteGstValue(nodeId));
        builder.Append(' ');
        if (pipeWireRemoteFd.HasValue && pipeWireRemoteFd.Value >= 0)
        {
            builder.Append("fd=");
            builder.Append(pipeWireRemoteFd.Value.ToString());
            builder.Append(' ');
        }

        builder.Append("always-copy=false use-bufferpool=true do-timestamp=true ");
        if (includeConversionPipeline)
        {
            builder.Append("! videorate drop-only=true max-rate=");
            builder.Append(MaximumPreviewFrameRate.ToString());
            builder.Append(" ! videoconvert ");
            builder.Append("! videoscale add-borders=false ");
        }

        builder.Append("! video/x-raw,format=BGRA,width=");
        builder.Append(widthPx.ToString());
        builder.Append(",height=");
        builder.Append(heightPx.ToString());
        builder.Append(",pixel-aspect-ratio=1/1 ");
        builder.Append("! queue leaky=downstream max-size-buffers=");
        builder.Append(QueueBufferCount.ToString());
        builder.Append(" max-size-bytes=0 max-size-time=0 ");
        builder.Append("! appsink name=");
        builder.Append(SinkName);
        builder.Append(" emit-signals=false sync=false max-buffers=");
        builder.Append(QueueBufferCount.ToString());
        builder.Append(" drop=true");
        return builder.ToString();
    }

    private static string QuoteGstValue(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private sealed class GStreamerAppSinkPipeWireRawBgraStream : IPipeWireRawBgraStream
    {
        private const ulong MillisecondInNanoseconds = 1_000_000;
        private const int GstMapRead = 1;
        private const int GstStateNull = 1;
        private const int GstStatePlaying = 4;
        private const int GstStateChangeFailure = 0;
        private const int FastPathProbeTimeoutMs = 350;
        private static readonly Lock InitSync = new();
        private static bool _initialized;

        private readonly IntPtr _pipeline;
        private readonly IntPtr _appSink;
        private readonly Lock _disposeSync = new();
        private bool _disposed;
        private bool _faulted;

        private GStreamerAppSinkPipeWireRawBgraStream(IntPtr pipeline, IntPtr appSink)
        {
            _pipeline = pipeline;
            _appSink = appSink;
        }

        public bool IsFaulted
        {
            get
            {
                lock (_disposeSync)
                    return _faulted || _disposed;
            }
        }

        public static IPipeWireRawBgraStream? TryStart(
            string nodeId,
            int widthPx,
            int heightPx,
            int? pipeWireRemoteFd
        )
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return null;

            try
            {
                EnsureInitialized();
                if (pipeWireRemoteFd.HasValue)
                {
                    // KDE's portal stream negotiates through the conversion path. Starting an
                    // additional raw pipeline first consumes time and can invalidate the stream.
                    return TryStartPipeline(
                        nodeId,
                        widthPx,
                        heightPx,
                        pipeWireRemoteFd,
                        includeConversionPipeline: true
                    );
                }

                IPipeWireRawBgraStream? fastPathStream = TryStartPipeline(
                    nodeId,
                    widthPx,
                    heightPx,
                    pipeWireRemoteFd,
                    includeConversionPipeline: false
                );
                if (fastPathStream is not null)
                {
                    using PipeWireRawBgraFrame? probeFrame = fastPathStream.PullFrame(
                        FastPathProbeTimeoutMs,
                        CancellationToken.None
                    );
                    if (probeFrame is not null)
                        return fastPathStream;

                    fastPathStream.Dispose();
                }

                return TryStartPipeline(
                    nodeId,
                    widthPx,
                    heightPx,
                    pipeWireRemoteFd,
                    includeConversionPipeline: true
                );
            }
            catch
            {
                return null;
            }
        }

        private static IPipeWireRawBgraStream? TryStartPipeline(
            string nodeId,
            int widthPx,
            int heightPx,
            int? pipeWireRemoteFd,
            bool includeConversionPipeline
        )
        {
            int? pipelineRemoteFd = DuplicatePipeWireRemoteFd(pipeWireRemoteFd);
            if (pipeWireRemoteFd.HasValue && !pipelineRemoteFd.HasValue)
                return null;

            string pipelineDescription = BuildPipeWirePipelineDescription(
                nodeId,
                widthPx,
                heightPx,
                pipelineRemoteFd,
                includeConversionPipeline
            );

            IntPtr error = IntPtr.Zero;
            IntPtr pipeline = GstParseLaunch(pipelineDescription, ref error);
            if (error != IntPtr.Zero)
                GErrorFree(error);
            if (pipeline == IntPtr.Zero)
                return null;

            IntPtr appSink = GstBinGetByName(pipeline, SinkName);
            if (appSink == IntPtr.Zero)
            {
                GstObjectUnref(pipeline);
                return null;
            }

            int stateResult = GstElementSetState(pipeline, GstStatePlaying);
            if (stateResult == GstStateChangeFailure)
            {
                GstObjectUnref(appSink);
                GstObjectUnref(pipeline);
                return null;
            }

            return new GStreamerAppSinkPipeWireRawBgraStream(pipeline, appSink);
        }

        private static int? DuplicatePipeWireRemoteFd(int? pipeWireRemoteFd)
        {
            if (!pipeWireRemoteFd.HasValue || pipeWireRemoteFd.Value < 0)
                return null;

            int duplicatedFd = DuplicateFileDescriptor(pipeWireRemoteFd.Value);
            return duplicatedFd >= 0 ? duplicatedFd : null;
        }

        public PipeWireRawBgraFrame? PullFrame(int timeoutMs, CancellationToken cancellationToken)
        {
            if (timeoutMs < 0)
                timeoutMs = 0;

            long deadlineMs = Environment.TickCount64 + timeoutMs;
            while (!cancellationToken.IsCancellationRequested)
            {
                IntPtr appSink;
                lock (_disposeSync)
                {
                    if (_disposed || _faulted)
                        return null;

                    appSink = _appSink;
                }

                int waitMs = Math.Min(100, Math.Max(1, timeoutMs));
                IntPtr sample = GstAppSinkTryPullSample(
                    appSink,
                    (ulong)waitMs * MillisecondInNanoseconds
                );
                if (sample != IntPtr.Zero)
                    return TryMapSample(sample);

                if (timeoutMs == 0 || Environment.TickCount64 >= deadlineMs)
                    return null;
            }

            return null;
        }

        public void Dispose()
        {
            bool shouldDispose;
            lock (_disposeSync)
            {
                shouldDispose = !_disposed;
                _disposed = true;
            }

            if (!shouldDispose)
                return;

            try
            {
                _ = GstElementSetState(_pipeline, GstStateNull);
            }
            catch { }

            try
            {
                GstObjectUnref(_appSink);
            }
            catch { }

            try
            {
                GstObjectUnref(_pipeline);
            }
            catch { }
        }

        private PipeWireRawBgraFrame? TryMapSample(IntPtr sample)
        {
            IntPtr buffer = IntPtr.Zero;
            bool mapped = false;
            try
            {
                buffer = GstSampleGetBuffer(sample);
                IntPtr caps = GstSampleGetCaps(sample);
                if (buffer == IntPtr.Zero || caps == IntPtr.Zero)
                    return null;

                IntPtr structure = GstCapsGetStructure(caps, 0);
                if (
                    structure == IntPtr.Zero
                    || !GstStructureGetInt(structure, "width", out int widthPx)
                    || !GstStructureGetInt(structure, "height", out int heightPx)
                    || widthPx <= 0
                    || heightPx <= 0
                )
                {
                    return null;
                }

                if (!GstBufferMap(buffer, out GstMapInfo map, GstMapRead))
                    return null;

                mapped = true;
                ulong size = map.Size.ToUInt64();
                if (size == 0 || size > int.MaxValue || map.Data == IntPtr.Zero)
                {
                    GstBufferUnmap(buffer, ref map);
                    mapped = false;
                    return null;
                }

                return new PipeWireRawBgraFrame(
                    map.Data,
                    (int)size,
                    widthPx,
                    heightPx,
                    () =>
                    {
                        GstBufferUnmap(buffer, ref map);
                        GstSampleUnref(sample);
                    }
                );
            }
            catch
            {
                MarkFaulted();
                return null;
            }
            finally
            {
                if (!mapped)
                    GstSampleUnref(sample);
            }
        }

        private void MarkFaulted()
        {
            lock (_disposeSync)
                _faulted = true;
        }

        private static void EnsureInitialized()
        {
            lock (InitSync)
            {
                if (_initialized)
                    return;

                GstInit(IntPtr.Zero, IntPtr.Zero);
                _initialized = true;
            }
        }

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_init")]
        private static extern void GstInit(IntPtr argc, IntPtr argv);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_parse_launch")]
        private static extern IntPtr GstParseLaunch(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string pipelineDescription,
            ref IntPtr error
        );

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_element_set_state")]
        private static extern int GstElementSetState(IntPtr element, int state);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_bin_get_by_name")]
        private static extern IntPtr GstBinGetByName(
            IntPtr bin,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name
        );

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_object_unref")]
        private static extern void GstObjectUnref(IntPtr obj);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_sample_get_buffer")]
        private static extern IntPtr GstSampleGetBuffer(IntPtr sample);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_sample_get_caps")]
        private static extern IntPtr GstSampleGetCaps(IntPtr sample);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_sample_unref")]
        private static extern void GstSampleUnref(IntPtr sample);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_caps_get_structure")]
        private static extern IntPtr GstCapsGetStructure(IntPtr caps, uint index);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_structure_get_int")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GstStructureGetInt(
            IntPtr structure,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string fieldName,
            out int value
        );

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_buffer_map")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GstBufferMap(IntPtr buffer, out GstMapInfo info, int flags);

        [DllImport("libgstreamer-1.0.so.0", EntryPoint = "gst_buffer_unmap")]
        private static extern void GstBufferUnmap(IntPtr buffer, ref GstMapInfo info);

        [DllImport("libgstapp-1.0.so.0", EntryPoint = "gst_app_sink_try_pull_sample")]
        private static extern IntPtr GstAppSinkTryPullSample(IntPtr appSink, ulong timeout);

        [DllImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
        private static extern void GErrorFree(IntPtr error);

        [DllImport("libc", EntryPoint = "dup")]
        private static extern int DuplicateFileDescriptor(int fileDescriptor);

        [StructLayout(LayoutKind.Sequential)]
        private struct GstMapInfo
        {
            public IntPtr Memory;
            public int Flags;
            public IntPtr Data;
            public UIntPtr Size;
            public UIntPtr MaxSize;
            public IntPtr UserData0;
            public IntPtr UserData1;
            public IntPtr UserData2;
            public IntPtr UserData3;
            public IntPtr Reserved0;
            public IntPtr Reserved1;
            public IntPtr Reserved2;
            public IntPtr Reserved3;
        }
    }
}
