using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal static class ObsPipeWireNative
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
        int stride,
        uint width,
        uint height,
        PixelFormat pixelFormat
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void StateCallback(
        IntPtr userData,
        int state,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? message
    );

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
        EntryPoint = "ws_pipewire_stream_update_target"
    )]
    internal static extern int UpdateTarget(IntPtr stream, uint width, uint height);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_is_faulted"
    )]
    internal static extern int IsFaulted(IntPtr stream);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "ws_pipewire_stream_destroy"
    )]
    internal static extern void DestroyStream(IntPtr stream);
}
