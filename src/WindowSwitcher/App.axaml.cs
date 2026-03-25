using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Sentry;
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
    private IDisposable? SentrySdkHandle { get; set; }

    public App()
    {
        TryInitializeSentry();
    }

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
        _ = StartGlobalKeyboardPipelineAsync(
            GlobalWindowKeybindRuntimeService,
            GlobalKeyboardService,
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

    private void OpenMenuItemClicked(object? sender, EventArgs e)
    {
        MainWindow?.RestoreFromTray();
    }

    private void TrayIconClicked(object? sender, EventArgs e)
    {
        MainWindow?.RestoreFromTray();
    }

    private static async Task StartGlobalKeyboardPipelineAsync(
        IGlobalWindowKeybindRuntimeService runtimeService,
        IGlobalKeyboardService globalKeyboardService,
        CancellationToken cancellationToken)
    {
        await StartGlobalWindowKeybindRuntimeServiceAsync(runtimeService, cancellationToken)
            .ConfigureAwait(false);
        await StartGlobalKeyboardServiceAsync(globalKeyboardService, cancellationToken)
            .ConfigureAwait(false);
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
            GlobalKeyboardCts = null;
            await ShutdownSentryAsync().ConfigureAwait(false);
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

        await ShutdownSentryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Initialize Sentry if enabled AND sentryDsn is provided
    /// </summary>
    private void TryInitializeSentry()
    {
        (bool enableSentry, string sentryDsn) = ConfigFileAccessor.GetInstance().ReadConfig(config =>
            (config.EnableSentry, config.SentryDsn)
        );
        if (!enableSentry)
            return;

        if (string.IsNullOrWhiteSpace(sentryDsn))
            return;

        try
        {
            SentrySdkHandle = SentrySdk.Init(options =>
            {
                options.Dsn = sentryDsn.Trim();
#if DEBUG
                options.Debug = true;
#endif
            });

            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to initialize cleanly: {ex.Message}");
        }
    }

    private void CaptureExceptionWithSentry(Exception exception)
    {
        if (SentrySdkHandle is null)
            return;

        try
        {
            SentrySdk.CaptureException(exception);
        }
        catch (Exception sentryException)
        {
            Trace.TraceWarning(
                $"[Sentry] Failed to capture exception cleanly: {sentryException.Message}"
            );
        }
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CaptureExceptionWithSentry(e.Exception);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CaptureExceptionWithSentry(CreateUnhandledException(e.ExceptionObject));
    }

    private void OnTaskSchedulerUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CaptureExceptionWithSentry(e.Exception);
    }

    internal static Exception CreateUnhandledException(object? exceptionObject)
    {
        return exceptionObject as Exception
            ?? new InvalidOperationException(
                $"Unhandled exception payload was not an Exception instance: {exceptionObject?.GetType().FullName ?? "null"}"
            );
    }

    private async Task ShutdownSentryAsync()
    {
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;

        if (SentrySdkHandle is null)
            return;

        try
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to flush events cleanly: {ex.Message}");
        }
        finally
        {
            SentrySdkHandle.Dispose();
            SentrySdkHandle = null;
        }
    }
}
