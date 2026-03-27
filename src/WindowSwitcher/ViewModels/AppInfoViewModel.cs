using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Data.Updates.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.ViewModels;

public partial class AppInfoViewModel : ObservableObject
{
    private readonly IPlatformAppInfoProvider _appInfoProvider;
    private readonly IAppUpdateService _appUpdateService;
    private readonly Action _requestApplicationShutdown;
    private UpdateCheckResult? _lastUpdateCheckResult;
    private DateTimeOffset _lastUpdateCheckUtc = DateTimeOffset.MinValue;

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

    [ObservableProperty]
    private string _updateStatus = "No update check yet.";

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _isStartingUpdate;

    public bool CanUpdateNow => IsUpdateAvailable && !IsCheckingForUpdates && !IsStartingUpdate;

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IAsyncRelayCommand UpdateNowCommand { get; }

    public AppInfoViewModel(
        IPlatformAppInfoProvider appInfoProvider,
        IAppUpdateService appUpdateService,
        Action requestApplicationShutdown
    )
    {
        ArgumentNullException.ThrowIfNull(appInfoProvider);
        ArgumentNullException.ThrowIfNull(appUpdateService);
        ArgumentNullException.ThrowIfNull(requestApplicationShutdown);

        _appInfoProvider = appInfoProvider;
        _appUpdateService = appUpdateService;
        _requestApplicationShutdown = requestApplicationShutdown;

        CheckForUpdatesCommand = new AsyncRelayCommand(
            () => CheckForUpdatesAsync(force: true, showUpToDateMessage: true),
            () => !IsCheckingForUpdates && !IsStartingUpdate
        );
        UpdateNowCommand = new AsyncRelayCommand(
            StartUpdateAsync,
            () => CanUpdateNow
        );

        Refresh();
    }

    public void Refresh()
    {
        AppVersion = GetApplicationVersion();
        PlatformAppInfoSnapshot snapshot = _appInfoProvider.GetSnapshot();
        OsDescription = snapshot.OsDescription;
        FrameworkDescription = snapshot.FrameworkDescription;
        ProcessArchitecture = snapshot.ProcessArchitecture;
        UiBackend = snapshot.UiBackend;
        ConfigPath = snapshot.ConfigPath;
        LinuxDependenciesStatus = snapshot.DependencyStatus;
        PreviewMode = snapshot.PreviewMode;
    }

    public async Task<bool> CheckForUpdatesAsync(
        bool force,
        bool showUpToDateMessage,
        CancellationToken cancellationToken = default
    )
    {
        if (IsCheckingForUpdates)
            return IsUpdateAvailable;

        // Avoid frequent network calls when this method is called repeatedly by UI refresh paths.
        if (
            !force
            && _lastUpdateCheckResult is not null
            && DateTimeOffset.UtcNow - _lastUpdateCheckUtc < TimeSpan.FromMinutes(5)
        )
        {
            return _lastUpdateCheckResult.IsUpdateAvailable;
        }

        IsCheckingForUpdates = true;

        try
        {
            UpdateStatus = "Checking for updates...";
            UpdateCheckResult result = await _appUpdateService
                .CheckForUpdatesAsync(AppVersion, cancellationToken)
                .ConfigureAwait(true);
            _lastUpdateCheckResult = result;
            _lastUpdateCheckUtc = DateTimeOffset.UtcNow;

            IsUpdateAvailable = result.IsUpdateAvailable;
            LatestVersion = result.LatestVersion ?? string.Empty;

            if (result.IsUpdateAvailable)
            {
                UpdateStatus = string.IsNullOrWhiteSpace(result.LatestVersion)
                    ? "A new version is available."
                    : $"Version {result.LatestVersion} is available.";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                UpdateStatus = $"Update check failed: {result.Message}";
                return false;
            }

            UpdateStatus = showUpToDateMessage ? "You are up to date." : "No update available.";
            return false;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task StartUpdateAsync()
    {
        if (!CanUpdateNow)
        {
            await CheckForUpdatesAsync(force: true, showUpToDateMessage: true).ConfigureAwait(true);
            if (!CanUpdateNow)
                return;
        }

        UpdateCheckResult? updateCheckResult = _lastUpdateCheckResult;
        if (updateCheckResult is null)
            return;

        IsStartingUpdate = true;
        try
        {
            UpdateStatus = "Preparing update...";
            UpdateLaunchResult launchResult = await _appUpdateService
                .LaunchUpdateAsync(updateCheckResult)
                .ConfigureAwait(true);
            if (!launchResult.Launched)
            {
                UpdateStatus = $"Unable to start update: {launchResult.Message ?? "unknown error"}";
                return;
            }

            UpdateStatus = "Update target started. Closing application...";
            _requestApplicationShutdown();
        }
        finally
        {
            IsStartingUpdate = false;
        }
    }

    partial void OnIsUpdateAvailableChanged(bool value)
    {
        NotifyUpdateActionStateChanged();
    }

    partial void OnIsCheckingForUpdatesChanged(bool value)
    {
        NotifyUpdateActionStateChanged();
    }

    partial void OnIsStartingUpdateChanged(bool value)
    {
        NotifyUpdateActionStateChanged();
    }

    private void NotifyUpdateActionStateChanged()
    {
        OnPropertyChanged(nameof(CanUpdateNow));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        UpdateNowCommand.NotifyCanExecuteChanged();
    }

    private static string GetApplicationVersion()
    {
        Assembly assembly = typeof(AppInfoViewModel).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            int metadataSeparatorIndex = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataSeparatorIndex >= 0
                ? informationalVersion[..metadataSeparatorIndex]
                : informationalVersion;
        }

        Version? assemblyVersion = assembly.GetName().Version;
        return assemblyVersion?.ToString(3) ?? string.Empty;
    }
}
