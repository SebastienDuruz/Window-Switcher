using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

/// <summary>
/// Windows diagnostics provider for application info view.
/// </summary>
public sealed class WindowsPlatformAppInfoProvider : IPlatformAppInfoProvider
{
    /// <inheritdoc />
    public PlatformAppInfoSnapshot GetSnapshot()
    {
        return new PlatformAppInfoSnapshot(
            OsDescription: RuntimeInformation.OSDescription,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            UiBackend: "Windows",
            ConfigPath: ConfigFileAccessor.GetInstance().GetFilePath(),
            PreviewMode: "Desktop Window Manager (DWM)",
            DependencyStatus: "N/A"
        );
    }
}
