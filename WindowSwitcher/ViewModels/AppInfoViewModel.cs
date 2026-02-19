using System;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public partial class AppInfoViewModel : ObservableObject
{
    private readonly IPlatformAppInfoProvider _appInfoProvider;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _osDescription = string.Empty;

    [ObservableProperty]
    private string _frameworkDescription = string.Empty;

    [ObservableProperty]
    private string _processArchitecture = string.Empty;

    [ObservableProperty]
    private string _uiBackend = "Unknown";

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _previewMode = string.Empty;

    [ObservableProperty]
    private string _linuxDependenciesStatus = string.Empty;

    public AppInfoViewModel(IPlatformAppInfoProvider appInfoProvider)
    {
        ArgumentNullException.ThrowIfNull(appInfoProvider);
        _appInfoProvider = appInfoProvider;
        Refresh();
    }

    public void Refresh()
    {
        AppVersion = "0.6.3";
        PlatformAppInfoSnapshot snapshot = _appInfoProvider.GetSnapshot();
        OsDescription = snapshot.OsDescription;
        FrameworkDescription = snapshot.FrameworkDescription;
        ProcessArchitecture = snapshot.ProcessArchitecture;
        UiBackend = snapshot.UiBackend;
        ConfigPath = snapshot.ConfigPath;
        LinuxDependenciesStatus = snapshot.DependencyStatus;
        PreviewMode = snapshot.PreviewMode;
    }
}
