using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Serilog;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.ViewModels;

namespace WindowSwitcher.Windows.Services;

internal sealed class StartupUpdateNotificationService
{
    private static readonly ILogger Logger = Log.ForContext<StartupUpdateNotificationService>();
    private readonly Window _ownerWindow;
    private readonly SettingsWindow _settingsWindow;

    public StartupUpdateNotificationService(Window ownerWindow, SettingsWindow settingsWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        ArgumentNullException.ThrowIfNull(settingsWindow);

        _ownerWindow = ownerWindow;
        _settingsWindow = settingsWindow;
    }

    public async Task NotifyUpdateAvailabilityOnStartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            bool isUpdateAvailable = await _settingsWindow.CheckForUpdatesAsync(
                force: true,
                showUpToDateMessage: false,
                cancellationToken
            );
            if (!isUpdateAvailable || StaticData.AppClosing)
                return;

            _settingsWindow.ShowPage(SettingsPage.About, _ownerWindow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path: the application is closing, the check is no longer needed.
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Startup update check failed");
        }
    }
}
