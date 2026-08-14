using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

/// <summary>
/// Linux diagnostics provider for application info view.
/// </summary>
public sealed class LinuxPlatformAppInfoProvider
    : IPlatformAppInfoProvider
{
    private readonly PlatformCapabilityStatus _capabilityStatus;

    /// <summary>
    /// Creates a diagnostics provider from the selected runtime capabilities.
    /// </summary>
    public LinuxPlatformAppInfoProvider(
        PlatformCapabilityStatus capabilityStatus,
        IPreviewFrameProvider previewFrameProvider
    )
    {
        ArgumentNullException.ThrowIfNull(capabilityStatus);
        ArgumentNullException.ThrowIfNull(previewFrameProvider);
        _capabilityStatus = capabilityStatus;
    }

    /// <inheritdoc />
    public PlatformAppInfoSnapshot GetSnapshot()
    {
        PlatformCapabilitySnapshot capability = _capabilityStatus.Current;
        var statuses = new List<string>
        {
            $"session: {FormatSession(capability.Session)}",
            $"window-control: {capability.WindowBackend}",
            $"preview: {capability.PreviewBackend}",
        };
        if (!string.IsNullOrWhiteSpace(capability.LastFailure))
            statuses.Add($"last-failure: {capability.LastFailure}");

        IReadOnlyCollection<string> reported = LinuxDependencies.GetReportedMissing();
        if (reported.Count > 0)
        {
            statuses.Add("reported-missing:");
            statuses.AddRange(reported.Order(StringComparer.OrdinalIgnoreCase));
        }

        return new PlatformAppInfoSnapshot(
            OsDescription: RuntimeInformation.OSDescription,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            UiBackend: FormatSession(capability.Session),
            ConfigPath: ConfigFileAccessor.GetInstance().GetFilePath(),
            PreviewMode: capability.PreviewAvailable
                ? capability.PreviewBackend
                : "Unavailable",
            DependencyStatus: string.Join(Environment.NewLine, statuses)
        );
    }

    private static string FormatSession(LinuxSessionKind session)
    {
        return session switch
        {
            LinuxSessionKind.X11 => "X11",
            LinuxSessionKind.Wayland => "Wayland",
            _ => "Unsupported",
        };
    }
}
