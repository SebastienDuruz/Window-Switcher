using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal static class LibPipeWireNative
{
    internal const string LibraryName = "libpipewire-0.3.so.0";
    internal const uint PipeWireIdCore = 0;

    internal static bool IsAvailable()
    {
        if (!OperatingSystem.IsLinux())
            return false;
        if (!NativeLibrary.TryLoad(LibraryName, out IntPtr handle))
            return false;
        NativeLibrary.Free(handle);
        return true;
    }
}
