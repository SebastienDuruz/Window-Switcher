using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal static class WindowSwitcherPipeWireNative
{
    internal const string LibraryName = "libwindowswitcher-pipewire.so";

    internal enum PixelFormat : uint
    {
        Bgra = 0,
        Bgrx = 1,
        Rgba = 2,
        Rgbx = 3,
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FrameCallback(
        IntPtr userData,
        IntPtr data,
        uint accessibleSize,
        int dmaBufFileDescriptor,
        uint offset,
        int stride,
        uint width,
        uint height,
        PixelFormat pixelFormat,
        uint drmFormat,
        ulong modifier,
        IntPtr frameLease
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StateCallback(
        IntPtr userData,
        int state,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? message
    );

    internal static bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux())
            return false;
        if (
            !NativeLibrary.TryLoad(
                LibraryName,
                typeof(WindowSwitcherPipeWireNative).Assembly,
                DllImportSearchPath.AssemblyDirectory,
                out IntPtr handle
            )
        )
            return false;
        NativeLibrary.Free(handle);
        return true;
    }

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_create"
    )]
    internal static extern IntPtr CreateStream(
        int portalFileDescriptor,
        uint nodeId,
        uint targetWidth,
        uint targetHeight,
        uint maximumFramerate,
        [In] uint[] dmaBufFormats,
        [In] ulong[] dmaBufModifiers,
        uint dmaBufCapabilityCount,
        FrameCallback frameCallback,
        StateCallback stateCallback,
        IntPtr userData
    );

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_set_active"
    )]
    internal static extern int SetActive(IntPtr stream, int active);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_set_dma_buf_enabled"
    )]
    internal static extern int SetDmaBufEnabled(IntPtr stream, int enabled);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_update_target"
    )]
    internal static extern int UpdateTarget(IntPtr stream, uint width, uint height);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_destroy"
    )]
    internal static extern void DestroyStream(IntPtr stream);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_frame_release"
    )]
    internal static extern void ReleaseFrame(IntPtr frameLease);
}
