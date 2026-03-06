using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Diagnostics;

/// <summary>
/// Linux diagnostics provider for application info view.
/// </summary>
public sealed class LinuxPlatformAppInfoProvider : IPlatformAppInfoProvider
{
    public PlatformAppInfoSnapshot GetSnapshot()
    {
        string[] statuses =
        [
            $"wmctrl: {(LinuxDependencies.IsWmctrlAvailable ? "OK" : "missing")}",
            $"import: {(LinuxDependencies.IsImportAvailable ? "OK" : "missing")}",
            $"gst-launch-1.0: {(LinuxDependencies.IsGstLaunchAvailable ? "OK" : "missing")}",
            $"gstreamer pipewiresrc: {(LinuxDependencies.IsGstPipeWireSrcAvailable ? "OK" : "missing")}",
            $"gdbus: {(LinuxDependencies.IsGdbusAvailable ? "OK" : "missing")}",
            $"pw-dump: {(LinuxDependencies.IsPwDumpAvailable ? "OK" : "missing")}",
        ];

        var reported = LinuxDependencies.GetReportedMissing().ToArray();
        string dependencies =
            reported.Length == 0
                ? string.Join(", ", statuses)
                : $"{string.Join(", ", statuses)}\nReported missing: {string.Join(", ", reported)}";

        bool pipeWireReady =
            LinuxDependencies.IsGstLaunchAvailable
            && LinuxDependencies.IsGstPipeWireSrcAvailable
            && LinuxDependencies.IsPwDumpAvailable;

        return new PlatformAppInfoSnapshot(
            OsDescription: RuntimeInformation.OSDescription,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            UiBackend: GetLinuxBackend(),
            ConfigPath: ConfigFileAccessor.GetInstance().GetFilePath(),
            PreviewMode: pipeWireReady ? "PipeWire" : "Screenshots",
            DependencyStatus: dependencies
        );
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
