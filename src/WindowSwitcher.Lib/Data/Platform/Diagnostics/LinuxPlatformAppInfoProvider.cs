using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

/// <summary>
/// Linux diagnostics provider for application info view.
/// </summary>
public sealed class LinuxPlatformAppInfoProvider : IPlatformAppInfoProvider
{
    /// <inheritdoc />
    public PlatformAppInfoSnapshot GetSnapshot()
    {
        string[] statuses =
        [
            $"wmctrl: {(LinuxDependencies.IsWmctrlAvailable ? "OK" : "missing")}",
            $"xcomposite: {(X11PreviewFrameProvider.IsSupported() ? "OK" : "missing")}",
            $"gstreamer pipewiresrc: {(LinuxDependencies.IsGstPipeWireSrcAvailable ? "OK" : "missing")}",
            $"gdbus: {(LinuxDependencies.IsGdbusAvailable ? "OK" : "missing")}",
            $"pw-dump: {(LinuxDependencies.IsPwDumpAvailable ? "OK" : "missing")}",
        ];

        var reported = LinuxDependencies.GetReportedMissing().ToArray();
        string dependencies =
            reported.Length == 0
                ? string.Join(Environment.NewLine, statuses)
                : $"{string.Join(Environment.NewLine, statuses)}{Environment.NewLine}Reported missing:{Environment.NewLine}{string.Join(Environment.NewLine, reported)}";

        return new PlatformAppInfoSnapshot(
            OsDescription: RuntimeInformation.OSDescription,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            UiBackend: GetLinuxBackend(),
            ConfigPath: ConfigFileAccessor.GetInstance().GetFilePath(),
            PreviewMode: GetPreviewMode(),
            DependencyStatus: dependencies
        );
    }

    private static string GetPreviewMode()
    {
        string? sessionType = LinuxSessionDetector.GetSessionType();
        if (string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase))
            return X11PreviewFrameProvider.IsSupported() ? "XComposite" : "Unavailable";

        bool pipeWireReady =
            LinuxDependencies.IsGstPipeWireSrcAvailable && LinuxDependencies.IsPwDumpAvailable;
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
            return pipeWireReady ? "PipeWire" : "Unavailable";

        if (X11PreviewFrameProvider.IsSupported())
            return "XComposite";

        return pipeWireReady ? "PipeWire" : "Unavailable";
    }

    private static string GetLinuxBackend()
    {
        if (
            Avalonia.Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime
        )
            return "Unknown";

        string? sessionType = LinuxSessionDetector.GetSessionType();
        return sessionType is null ? "Unknown" : sessionType.ToUpperInvariant();
    }
}
