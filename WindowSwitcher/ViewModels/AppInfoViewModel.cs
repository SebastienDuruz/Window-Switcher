using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data;
using WindowSwitcherLib.Data.Commands;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcher.ViewModels;

public partial class AppInfoViewModel : ObservableObject
{
    [ObservableProperty] private string _appName = StaticData.AppName;
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _osDescription = RuntimeInformation.OSDescription;
    [ObservableProperty] private string _frameworkDescription = RuntimeInformation.FrameworkDescription;
    [ObservableProperty] private string _processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
    [ObservableProperty] private string _uiBackend = "Unknown";
    [ObservableProperty] private string _configPath = string.Empty;
    [ObservableProperty] private string _linuxDependenciesStatus = string.Empty;
    [ObservableProperty] private string _previewMode = string.Empty;

    public AppInfoViewModel()
    {
        Refresh();
    }

    public void Refresh()
    {
        AppName = StaticData.AppName;
        AppVersion = "0.6.0";
        OsDescription = RuntimeInformation.OSDescription;
        FrameworkDescription = RuntimeInformation.FrameworkDescription;
        ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        UiBackend = GetUiBackend();
        ConfigPath = ConfigFileAccessor.GetInstance().GetFilePath();
        LinuxDependenciesStatus = GetLinuxDependencies();
        PreviewMode = GetLinuxPreviewMode();
    }

    private static string GetUiBackend()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return "Unknown";

        string? descriptor = desktop.MainWindow?.TryGetPlatformHandle()?.HandleDescriptor;
        if (string.Equals(descriptor, "XID", StringComparison.OrdinalIgnoreCase))
            return "X11 / XWayland (XID)";

        if (!string.IsNullOrWhiteSpace(descriptor))
            return $"Wayland ({descriptor})";

        return "Unknown";
    }

    private static string GetLinuxDependencies()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "N/A";

        string[] statuses =
        [
            $"wmctrl: {(LinuxDependencies.IsWmctrlAvailable ? "OK" : "missing")}",
            $"import: {(LinuxDependencies.IsImportAvailable ? "OK" : "missing")}",
        ];

        var reported = LinuxDependencies.GetReportedMissing().ToArray();
        if (reported.Length == 0)
            return string.Join(", ", statuses);

        return $"{string.Join(", ", statuses)}\nReported missing: {string.Join(", ", reported)}";
    }

    private static string GetLinuxPreviewMode()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "N/A";

        return "Screenshots (import)";
    }
}
