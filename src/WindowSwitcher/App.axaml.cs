using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Theming;

namespace WindowSwitcher;

public partial class App : Application
{
    private Windows.MainWindow? MainWindow { get; set; }
    private IGlobalKeyboardService? GlobalKeyboardService { get; set; }
    private IGlobalWindowKeybindRuntimeService? GlobalWindowKeybindRuntimeService { get; set; }
    private CancellationTokenSource? GlobalKeyboardCts { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StaticData.CheckFolders();
        ApplyAccentColorFromConfig();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new Windows.MainWindow();
            desktop.MainWindow = MainWindow;
            desktop.Exit += OnDesktopExit;
        }

        GlobalKeyboardService = AppServiceProvider.GetRequiredService<IGlobalKeyboardService>();
        GlobalWindowKeybindRuntimeService =
            AppServiceProvider.GetRequiredService<IGlobalWindowKeybindRuntimeService>();
        GlobalKeyboardCts = new CancellationTokenSource();
        _ = StartGlobalKeyboardServiceAsync(GlobalKeyboardService, GlobalKeyboardCts.Token);
        _ = StartGlobalWindowKeybindRuntimeServiceAsync(
            GlobalWindowKeybindRuntimeService,
            GlobalKeyboardCts.Token
        );

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyAccentColorFromConfig()
    {
        string configValue = ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => config.PreviewHighlightColor);
        if (Color.TryParse(configValue, out Color accentColor))
            AccentColorApplier.Apply(accentColor);
    }

    private void ExitMenuItemClicked(object? sender, EventArgs e)
    {
        MainWindow?.Close();
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        if (MainWindow is null)
            return;
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
    }

    private static async Task StartGlobalKeyboardServiceAsync(
        IGlobalKeyboardService globalKeyboardService,
        CancellationToken cancellationToken)
    {
        try
        {
            await globalKeyboardService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[GlobalKeyboard] Global keyboard listening is unavailable: {ex.Message}"
            );
        }
    }

    private static async Task StartGlobalWindowKeybindRuntimeServiceAsync(
        IGlobalWindowKeybindRuntimeService runtimeService,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtimeService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[GlobalKeyboard] Global keybind runtime is unavailable: {ex.Message}"
            );
        }
    }

    private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (GlobalKeyboardCts is not null && !GlobalKeyboardCts.IsCancellationRequested)
            GlobalKeyboardCts.Cancel();

        if (GlobalWindowKeybindRuntimeService is not null)
        {
            try
            {
                await GlobalWindowKeybindRuntimeService.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    $"[GlobalKeyboard] Failed to stop global keybind runtime cleanly: {ex.Message}"
                );
            }

            try
            {
                await GlobalWindowKeybindRuntimeService.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    $"[GlobalKeyboard] Failed to dispose global keybind runtime cleanly: {ex.Message}"
                );
            }
            finally
            {
                GlobalWindowKeybindRuntimeService = null;
            }
        }

        if (GlobalKeyboardService is null)
        {
            GlobalKeyboardCts?.Dispose();
            return;
        }

        try
        {
            await GlobalKeyboardService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[GlobalKeyboard] Failed to stop global keyboard listener cleanly: {ex.Message}"
            );
        }

        try
        {
            await GlobalKeyboardService.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[GlobalKeyboard] Failed to dispose global keyboard listener cleanly: {ex.Message}"
            );
        }
        finally
        {
            GlobalKeyboardCts?.Dispose();
            GlobalKeyboardCts = null;
            GlobalKeyboardService = null;
        }
    }
}
