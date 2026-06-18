using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Windows.Services;

internal sealed class StartupUpdateNotificationService
{
    private readonly Window _ownerWindow;
    private readonly AppInfoWindow _appInfoWindow;

    public StartupUpdateNotificationService(Window ownerWindow, AppInfoWindow appInfoWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(appInfoWindow);

        _ownerWindow = ownerWindow;
        _appInfoWindow = appInfoWindow;
    }

    public async Task NotifyUpdateAvailabilityOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            bool isUpdateAvailable = await _appInfoWindow.CheckForUpdatesAsync(
                force: true,
                showUpToDateMessage: false,
                cancellationToken
            );
            if (!isUpdateAvailable || StaticData.AppClosing)
                return;

            ShowAppInfoWindow();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Updates] Startup update check failed: {ex.Message}");
        }
    }

    private void ShowAppInfoWindow()
    {
        if (_appInfoWindow.IsVisible)
        {
            _appInfoWindow.Activate();
            return;
        }

        if (_ownerWindow.IsVisible)
        {
            _appInfoWindow.Show(_ownerWindow);
            return;
        }

        _appInfoWindow.Show();
    }
}
