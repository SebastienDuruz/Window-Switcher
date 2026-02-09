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
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _osDescription = RuntimeInformation.OSDescription;
    [ObservableProperty] private string _frameworkDescription = RuntimeInformation.FrameworkDescription;
    [ObservableProperty] private string _processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
    [ObservableProperty] private string _uiBackend = "Unknown";
    [ObservableProperty] private string _configPath = string.Empty;
    [ObservableProperty] private string _previewMode = string.Empty;
    [ObservableProperty] private string _linuxDependenciesStatus = string.Empty;

    public AppInfoViewModel()
    {
        Refresh();
    }

    public void Refresh()
    {
        AppVersion = "0.6.1";
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
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            return "Unknown";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? sessionType = GetLinuxSessionType();
            return sessionType is null ? "Unknown" : sessionType.ToUpperInvariant();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Windows";
        else
            return "Unknown";
    }

    private static string? GetLinuxSessionType()
    {
        var wrapper = new ShWrapper();
        string output = wrapper.Execute("echo $XDG_SESSION_TYPE").Trim();
        return string.IsNullOrWhiteSpace(output) ? null : output;
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
            return "Desktop Window Manager (DWM)";

        return "Screenshots (import)";
    }
}
