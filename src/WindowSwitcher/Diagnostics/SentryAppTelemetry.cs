using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sentry;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Diagnostics;

internal interface ITelemetrySettingsProvider
{
    (bool EnableSentry, string SentryDsn) GetSettings();
}

internal sealed class ConfigFileTelemetrySettingsProvider : ITelemetrySettingsProvider
{
    public (bool EnableSentry, string SentryDsn) GetSettings()
    {
        return ConfigFileAccessor.GetInstance().ReadConfig(config =>
            (
                config.EnableSentry,
                SentryAppTelemetry.ResolveSentryDsn(config.SentryDsn)
            )
        );
    }
}

internal sealed class SentryAppTelemetry : IAppTelemetry
{
    internal const string AppStartedMetricName = "window_switcher.app_started";

    private readonly ISentrySdkAdapter _sentrySdk;
    private readonly ITelemetrySettingsProvider _telemetrySettingsProvider;
    private readonly object _syncRoot = new();
    private IDisposable? _sdkHandle;
    private bool _appStartedRecorded;

    public SentryAppTelemetry(
        ISentrySdkAdapter sentrySdk,
        ITelemetrySettingsProvider telemetrySettingsProvider
    )
    {
        ArgumentNullException.ThrowIfNull(sentrySdk);
        ArgumentNullException.ThrowIfNull(telemetrySettingsProvider);

        _sentrySdk = sentrySdk;
        _telemetrySettingsProvider = telemetrySettingsProvider;
    }

    public void Initialize()
    {
        if (IsInitialized())
            return;

        (bool enableSentry, string sentryDsn) = _telemetrySettingsProvider.GetSettings();
        if (!enableSentry)
            return;

        try
        {
            IDisposable handle = _sentrySdk.Init(options => ConfigureOptions(options, sentryDsn));
            lock (_syncRoot)
            {
                if (_sdkHandle is not null)
                {
                    handle.Dispose();
                    return;
                }

                _sdkHandle = handle;
            }

            ConfigureBaseScope();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to initialize cleanly: {ex.Message}");
        }
    }

    public async Task RecordAppStartedAsync(string previewMode)
    {
        string normalizedPreviewMode = NormalizePreviewMode(previewMode);

        lock (_syncRoot)
        {
            if (_appStartedRecorded)
                return;

            _appStartedRecorded = true;
        }

        if (!IsInitialized())
            return;

        try
        {
            _sentrySdk.EmitCounter(
                AppStartedMetricName,
                1,
                CreateAppStartedMetricAttributes(normalizedPreviewMode)
            );
            await _sentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to capture app_started metric cleanly: {ex.Message}");
        }
    }

    public void CaptureUnhandledException(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!IsInitialized())
            return;

        try
        {
            _sentrySdk.CaptureException(exception, scope =>
                scope.SetTag("capture_source", NormalizeTagValue(source))
            );
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to capture exception cleanly: {ex.Message}");
        }
    }

    public async Task ShutdownAsync()
    {
        IDisposable? handle;
        lock (_syncRoot)
        {
            handle = _sdkHandle;
            _sdkHandle = null;
        }

        if (handle is null)
            return;

        try
        {
            await _sentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to flush events cleanly: {ex.Message}");
        }
        finally
        {
            handle.Dispose();
        }
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

    internal static string ResolveSentryDsn(string? configuredSentryDsn)
    {
        if (!string.IsNullOrWhiteSpace(configuredSentryDsn))
            return configuredSentryDsn.Trim();

        return new ConfigFile().SentryDsn;
    }

    internal static string NormalizePreviewMode(string? previewMode)
    {
        if (string.IsNullOrWhiteSpace(previewMode))
            return "unknown";

        string normalized = previewMode.Trim().ToLowerInvariant();
        if (normalized.Contains("pipewire", StringComparison.Ordinal))
            return "pipewire";
        if (normalized.Contains("screenshot", StringComparison.Ordinal))
            return "screenshots";
        if (
            normalized.Contains("desktop window manager", StringComparison.Ordinal)
            || normalized.Contains("(dwm)", StringComparison.Ordinal)
            || string.Equals(normalized, "dwm", StringComparison.Ordinal)
        )
            return "dwm";

        return "other";
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

        return terminalExceptions.All(IsIgnorableSentryException);
    }

    internal static IReadOnlyDictionary<string, string> CreateAppStartedMetricAttributes(
        string previewMode
    )
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["os"] = GetSentryOperatingSystemTag(),
            ["session_type"] = GetSentrySessionTypeTag(),
            ["build_channel"] = GetSentryEnvironment(),
            ["app_version"] = GetInformationalVersion(),
            ["preview_mode"] = NormalizePreviewMode(previewMode),
        };
    }

    private void ConfigureBaseScope()
    {
        _sentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("os", GetSentryOperatingSystemTag());
            scope.SetTag("session_type", GetSentrySessionTypeTag());
            scope.SetTag("app_version", GetInformationalVersion());
            scope.SetTag("build_channel", GetSentryEnvironment());
        });
    }

    private bool IsInitialized()
    {
        lock (_syncRoot)
        {
            return _sdkHandle is not null;
        }
    }

    private static void ConfigureOptions(SentryOptions options, string sentryDsn)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sentryDsn);

        options.Dsn = sentryDsn;
        options.Release = GetSentryRelease();
        options.Environment = GetSentryEnvironment();
        options.SendDefaultPii = false;
        options.DisableAppDomainUnhandledExceptionCapture();
        options.DisableUnobservedTaskExceptionCapture();
        options.DisableAppDomainProcessExitFlush();
        options.DisableSystemDiagnosticsMetricsIntegration();
        options.Experimental.EnableMetrics = true;
        options.SetBeforeSend(static (sentryEvent, _) => FilterSentryEvent(sentryEvent));
#if DEBUG
        options.Debug = true;
#endif
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
        return string.IsNullOrWhiteSpace(sessionType) ? "unknown" : NormalizeTagValue(sessionType);
    }

    private static string NormalizeTagValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
