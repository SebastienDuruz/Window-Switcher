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
    (string SentryDsn, string TelemetryUserId) GetSettings();
}

internal sealed class ConfigFileTelemetrySettingsProvider : ITelemetrySettingsProvider
{
    public (string SentryDsn, string TelemetryUserId) GetSettings()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config =>
                (
                    SentryAppTelemetry.ResolveSentryDsn(config.SentryDsn),
                    SentryAppTelemetry.ResolveTelemetryUserId(config.TelemetryUserId)
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
    private string _telemetryUserId = string.Empty;
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

        (string sentryDsn, string telemetryUserId) = _telemetrySettingsProvider.GetSettings();

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
                _telemetryUserId = telemetryUserId;
            }

            ConfigureBaseScope(telemetryUserId);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to initialize cleanly: {ex.Message}");
        }
    }

    public async Task RecordAppStartedAsync(string previewMode)
    {
        string normalizedPreviewMode = AppTelemetrySanitizer.NormalizePreviewMode(previewMode);
        string telemetryUserId;

        lock (_syncRoot)
        {
            if (_appStartedRecorded)
                return;

            _appStartedRecorded = true;
            telemetryUserId = _telemetryUserId;
        }

        if (!IsInitialized())
            return;

        try
        {
            _sentrySdk.EmitCounter(
                AppStartedMetricName,
                1,
                CreateAppStartedMetricAttributes(normalizedPreviewMode, telemetryUserId)
            );
            await _sentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[Sentry] Failed to capture app_started metric cleanly: {ex.Message}"
            );
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
            _sentrySdk.CaptureException(
                exception,
                scope => scope.SetTag("capture_source", NormalizeTagValue(source))
            );
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Sentry] Failed to capture exception cleanly: {ex.Message}");
        }
    }

    public void CaptureHandledException(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string>? tags = null
    )
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!IsInitialized())
            return;

        try
        {
            _sentrySdk.CaptureException(
                exception,
                scope =>
                {
                    scope.SetTag("capture_source", NormalizeTagValue(source));
                    scope.SetTag("handled", "true");

                    if (tags is null)
                        return;

                    foreach (KeyValuePair<string, string> tag in tags)
                    {
                        if (
                            string.IsNullOrWhiteSpace(tag.Key)
                            || string.IsNullOrWhiteSpace(tag.Value)
                        )
                            continue;

                        scope.SetTag(NormalizeTagKey(tag.Key), NormalizeTagValue(tag.Value));
                    }
                }
            );
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                $"[Sentry] Failed to capture handled exception cleanly: {ex.Message}"
            );
        }
    }

    public async Task ShutdownAsync()
    {
        IDisposable? handle;
        lock (_syncRoot)
        {
            handle = _sdkHandle;
            _sdkHandle = null;
            _telemetryUserId = string.Empty;
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

    internal static string ResolveTelemetryUserId(string? configuredTelemetryUserId)
    {
        if (Guid.TryParse(configuredTelemetryUserId, out Guid parsed))
            return parsed.ToString("D");

        return new ConfigFile().TelemetryUserId;
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
        string previewMode,
        string telemetryUserId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(telemetryUserId);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["os"] = GetSentryOperatingSystemTag(),
            ["session_type"] = GetSentrySessionTypeTag(),
            ["build_channel"] = GetSentryEnvironment(),
            ["app_version"] = GetInformationalVersion(),
            ["preview_mode"] = AppTelemetrySanitizer.NormalizePreviewMode(previewMode),
            ["telemetry_user_id"] = telemetryUserId,
        };
    }

    private void ConfigureBaseScope(string telemetryUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(telemetryUserId);

        _sentrySdk.ConfigureScope(scope =>
        {
            scope.User = new SentryUser { Id = telemetryUserId };
            scope.SetTag("os", GetSentryOperatingSystemTag());
            scope.SetTag("session_type", GetSentrySessionTypeTag());
            scope.SetTag("app_version", GetInformationalVersion());
            scope.SetTag("build_channel", GetSentryEnvironment());
            scope.SetTag("telemetry_user_id", telemetryUserId);
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
        options.EnableMetrics = true;
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
        return typeof(App)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
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

    private static string NormalizeTagKey(string value)
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
