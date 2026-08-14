using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using WindowSwitcher.Diagnostics;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Theming;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher;

public partial class App : Application
{
    private Windows.MainWindow? MainWindow { get; set; }
    private IGlobalKeyboardService? GlobalKeyboardService { get; set; }
    private IGlobalWindowKeybindRuntimeService? GlobalWindowKeybindRuntimeService { get; set; }
    private CancellationTokenSource? GlobalKeyboardCts { get; set; }
    private IAppTelemetry AppTelemetry { get; }

    public App()
    {
        AppServiceProvider.Initialize();
        AppTelemetry = AppServiceProvider.GetRequiredService<IAppTelemetry>();
        AppTelemetry.Initialize();
        CaptureConfigurationLoadFailureIfNeeded();
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CaptureConfigurationLoadFailureIfNeeded()
    {
        ConfigFileAccessor.ConfigLoadFailure? failure = ConfigFileAccessor
            .GetInstance()
            .ConsumeLastReadFailure();
        if (failure is null)
            return;

        var exception = new InvalidOperationException(
            "User settings could not be loaded and defaults were restored."
        );
        AppTelemetry.CaptureHandledException(
            exception,
            "config_load_failure",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reason"] = failure.Reason,
                ["exception_type"] = failure.ExceptionType,
                ["defaults_restored"] = failure.DefaultsRestored ? "true" : "false",
            }
        );
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StaticData.CheckFolders();
        ApplyAccentColorFromConfig();
        RecordApplicationStartupTelemetry();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new Windows.MainWindow();
            desktop.MainWindow = MainWindow;
            desktop.Exit += OnDesktopExit;
        }

        GlobalKeyboardService = AppServiceProvider.GetRequiredService<IGlobalKeyboardService>();
        GlobalWindowKeybindRuntimeService =
            AppServiceProvider.GetRequiredService<IGlobalWindowKeybindRuntimeService>();
        IGlobalKeyboardStartupStatusService globalKeyboardStartupStatusService =
            AppServiceProvider.GetRequiredService<IGlobalKeyboardStartupStatusService>();
        GlobalKeyboardCts = new CancellationTokenSource();
        _ = StartGlobalKeyboardPipelineAsync(
            GlobalWindowKeybindRuntimeService,
            GlobalKeyboardService,
            globalKeyboardStartupStatusService,
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

    private void RecordApplicationStartupTelemetry()
    {
        IPlatformAppInfoProvider appInfoProvider =
            AppServiceProvider.GetRequiredService<IPlatformAppInfoProvider>();
        string previewMode = AppTelemetrySanitizer.NormalizePreviewMode(
            appInfoProvider.GetSnapshot().PreviewMode
        );

        _ = AppTelemetry.RecordAppStartedAsync(previewMode);
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
        IGlobalKeyboardStartupStatusService globalKeyboardStartupStatusService,
        CancellationToken cancellationToken
    )
    {
        await StartGlobalWindowKeybindRuntimeServiceAsync(runtimeService, cancellationToken)
            .ConfigureAwait(false);
        await StartGlobalKeyboardServiceAsync(
                globalKeyboardService,
                globalKeyboardStartupStatusService,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task StartGlobalKeyboardServiceAsync(
        IGlobalKeyboardService globalKeyboardService,
        IGlobalKeyboardStartupStatusService globalKeyboardStartupStatusService,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await globalKeyboardService.StartAsync(cancellationToken).ConfigureAwait(false);
            globalKeyboardStartupStatusService.ReportStarted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            globalKeyboardStartupStatusService.ReportStartupFailure(ex);
        }
    }

    private static async Task StartGlobalWindowKeybindRuntimeServiceAsync(
        IGlobalWindowKeybindRuntimeService runtimeService,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await runtimeService.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception) { }
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
            catch (Exception) { }

            GlobalWindowKeybindRuntimeService = null;
        }

        if (GlobalKeyboardService is null)
        {
            GlobalKeyboardCts?.Dispose();
            GlobalKeyboardCts = null;
            await CompleteShutdownAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await GlobalKeyboardService.StopAsync().ConfigureAwait(false);
        }
        catch (Exception) { }

        GlobalKeyboardCts?.Dispose();
        GlobalKeyboardCts = null;
        GlobalKeyboardService = null;

        await CompleteShutdownAsync().ConfigureAwait(false);
    }

    private void OnDispatcherUnhandledException(
        object? sender,
        DispatcherUnhandledExceptionEventArgs e
    )
    {
        AppTelemetry.CaptureUnhandledException(e.Exception, "dispatcher_unhandled");
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppTelemetry.CaptureUnhandledException(
            AppTelemetrySanitizer.CreateUnhandledException(e.ExceptionObject),
            "appdomain_unhandled"
        );
    }

    private void OnTaskSchedulerUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e
    )
    {
        AppTelemetry.CaptureUnhandledException(e.Exception, "task_unobserved");
    }

    private async Task ShutdownTelemetryAsync()
    {
        Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;
        await AppTelemetry.ShutdownAsync().ConfigureAwait(false);
    }

    private async Task CompleteShutdownAsync()
    {
        await ShutdownTelemetryAsync().ConfigureAwait(false);
        await AppServiceProvider.DisposeAsync().ConfigureAwait(false);
    }
}
