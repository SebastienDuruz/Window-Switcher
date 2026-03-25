using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
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
                options.Release = GetSentryRelease();
                options.Environment = GetSentryEnvironment();
                options.SendDefaultPii = false;
                options.MaxBreadcrumbs = 30;
                options.DisableAppDomainUnhandledExceptionCapture();
                options.DisableUnobservedTaskExceptionCapture();
                options.DisableAppDomainProcessExitFlush();
                options.SetBeforeSend(static (sentryEvent, _) => FilterSentryEvent(sentryEvent));
#if DEBUG
                options.Debug = true;
#endif
            });

            ConfigureSentryScope();
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to initialize cleanly: {ex.Message}");
        }
    }

    private static void ConfigureSentryScope()
    {
        string operatingSystemTag = GetSentryOperatingSystemTag();
        string sessionTypeTag = GetSentrySessionTypeTag();
        string appVersionTag = GetInformationalVersion();
        string buildChannelTag = GetSentryEnvironment();

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("os", operatingSystemTag);
            scope.SetTag("session_type", sessionTypeTag);
            scope.SetTag("app_version", appVersionTag);
            scope.SetTag("build_channel", buildChannelTag);
        });
    }

    private void CaptureExceptionWithSentry(Exception exception, string source)
    {
        if (SentrySdkHandle is null)
            return;

        try
        {
            SentrySdk.CaptureException(exception, scope =>
            {
                scope.SetTag("capture_source", source);
            });
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
        CaptureExceptionWithSentry(e.Exception, "dispatcher_unhandled");
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CaptureExceptionWithSentry(
            CreateUnhandledException(e.ExceptionObject),
            "appdomain_unhandled"
        );
    }

    private void OnTaskSchedulerUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CaptureExceptionWithSentry(e.Exception, "task_unobserved");
    }

    internal static Exception CreateUnhandledException(object? exceptionObject)
    {
        return exceptionObject as Exception
            ?? new InvalidOperationException(
                $"Unhandled exception payload was not an Exception instance: {exceptionObject?.GetType().FullName ?? "null"}"
            );
    }

    internal static string GetSentryRelease()
    {
        return BuildSentryRelease(GetInformationalVersion());
    }

    internal static string BuildSentryRelease(string? informationalVersion)
    {
        string normalizedVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? "unknown"
            : informationalVersion.Trim();
        return $"window-switcher@{normalizedVersion}";
    }

    internal static string GetSentryEnvironment()
    {
        return IsDebugBuild() ? "debug" : "production";
    }

    internal static SentryEvent? FilterSentryEvent(SentryEvent sentryEvent)
    {
        ArgumentNullException.ThrowIfNull(sentryEvent);

        Exception? exception = sentryEvent.Exception;
        if (exception is null)
            return sentryEvent;

        return ShouldDropExceptionFromSentry(exception) ? null : sentryEvent;
    }

    internal static bool ShouldDropExceptionFromSentry(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception[] terminalExceptions = GetTerminalExceptions(exception).ToArray();
        if (terminalExceptions.Length == 0)
            return false;

        bool sawIgnorableException = false;
        for (int index = 0; index < terminalExceptions.Length; index++)
        {
            Exception terminalException = terminalExceptions[index];
            if (IsIgnorableSentryException(terminalException))
            {
                sawIgnorableException = true;
                continue;
            }

            return false;
        }

        return sawIgnorableException;
    }

    private static IEnumerable<Exception> GetTerminalExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            Exception current = pending.Pop();
            if (!visited.Add(current))
                continue;

            if (current is AggregateException aggregateException)
            {
                AggregateException flattened = aggregateException.Flatten();
                if (flattened.InnerExceptions.Count > 0)
                {
                    for (int index = flattened.InnerExceptions.Count - 1; index >= 0; index--)
                        pending.Push(flattened.InnerExceptions[index]);
                    continue;
                }
            }

            if (current.InnerException is Exception innerException)
            {
                pending.Push(innerException);
                continue;
            }

            yield return current;
        }
    }

    private static bool IsIgnorableSentryException(Exception exception)
    {
        if (exception is OperationCanceledException)
            return true;

        Type exceptionType = exception.GetType();
        string fullName = exceptionType.FullName ?? exceptionType.Name;
        return fullName.StartsWith("Tmds.DBus", StringComparison.Ordinal)
            && fullName.EndsWith("DisconnectedException", StringComparison.Ordinal);
    }

    private static string GetInformationalVersion()
    {
        return typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? typeof(App).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string GetSentryOperatingSystemTag()
    {
        if (OperatingSystem.IsLinux())
            return "linux";
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";

        return "unknown";
    }

    private static string GetSentrySessionTypeTag()
    {
        if (!OperatingSystem.IsLinux())
            return "not_applicable";

        string? sessionType = LinuxSessionDetector.GetSessionType();
        return string.IsNullOrWhiteSpace(sessionType) ? "unknown" : sessionType;
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
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
