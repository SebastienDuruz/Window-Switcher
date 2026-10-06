using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sentry;
using Serilog;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Diagnostics;

internal interface ITelemetrySettingsProvider
{
    (string SentryDsn, string TelemetryInstallationId) GetSettings();
}

internal sealed class ConfigFileTelemetrySettingsProvider : ITelemetrySettingsProvider
{
    public (string SentryDsn, string TelemetryInstallationId) GetSettings()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config =>
                (
                    SentryAppTelemetry.ResolveSentryDsn(config.SentryDsn),
                    SentryAppTelemetry.ResolveTelemetryInstallationId(
                        config.TelemetryInstallationId
                    )
                )
            );
    }
}

internal sealed class SentryAppTelemetry : IAppTelemetry
{
    internal const string AppStartedMetricName = AppTelemetryEvents.AppStarted;
    internal const string TelemetrySchemaVersion = "2";

    private static readonly ILogger Logger = Log.ForContext<SentryAppTelemetry>();

    private readonly ISentrySdkAdapter _sentrySdk;
    private readonly ITelemetrySettingsProvider _telemetrySettingsProvider;
    private readonly object _syncRoot = new();
    private IDisposable? _sdkHandle;
    private string _telemetryInstallationId = string.Empty;
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

        (string sentryDsn, string telemetryInstallationId) =
            _telemetrySettingsProvider.GetSettings();

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
                _telemetryInstallationId = telemetryInstallationId;
            }

            ConfigureBaseScope(telemetryInstallationId);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Sentry initialization failed; error reporting is disabled");
        }
    }

    public async Task RecordAppStartedAsync(string previewMode)
    {
        string normalizedPreviewMode = AppTelemetrySanitizer.NormalizePreviewMode(previewMode);
        string telemetryInstallationId;

        if (!IsInitialized())
            return;

        lock (_syncRoot)
        {
            if (_appStartedRecorded)
                return;

            _appStartedRecorded = true;
            telemetryInstallationId = _telemetryInstallationId;
        }

        try
        {
            _sentrySdk.EmitCounter(
                AppStartedMetricName,
                1,
                CreateMetricAttributes(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["preview_mode"] = normalizedPreviewMode,
                        ["telemetry_installation_id"] = telemetryInstallationId,
                    }
                )
            );
            await _sentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Sentry startup metric could not be sent");
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
        catch (Exception captureException)
        {
            // Debug only: this runs inside the global crash handlers and must stay side-effect free.
            Logger.Debug(captureException, "Sentry could not capture an unhandled exception");
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
        catch (Exception captureException)
        {
            Logger.Warning(captureException, "Sentry could not capture a handled exception");
        }
    }

    public async Task ShutdownAsync()
    {
        IDisposable? handle;
        lock (_syncRoot)
        {
            handle = _sdkHandle;
            _sdkHandle = null;
            _telemetryInstallationId = string.Empty;
        }

        if (handle is null)
            return;

        try
        {
            await _sentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Sentry flush failed during shutdown");
        }
        finally
        {
            handle.Dispose();
        }
    }

    internal static string GetSentryRelease()
    {
        return BuildSentryRelease(AppRuntimeInfo.GetInformationalVersion());
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
        return AppRuntimeInfo.GetBuildChannel();
    }

    internal static string ResolveSentryDsn(string? configuredSentryDsn)
    {
        if (!string.IsNullOrWhiteSpace(configuredSentryDsn))
            return configuredSentryDsn.Trim();

        return new ConfigFile().SentryDsn;
    }

    internal static string ResolveTelemetryInstallationId(string? configuredTelemetryInstallationId)
    {
        if (Guid.TryParse(configuredTelemetryInstallationId, out Guid parsed))
            return parsed.ToString("D");

        return new ConfigFile().TelemetryInstallationId;
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

    internal static IReadOnlyDictionary<string, string> CreateMetricAttributes(
        IReadOnlyDictionary<string, string>? attributes = null
    )
    {
        var mergedAttributes = new Dictionary<string, string>(
            CreateBaseTags(),
            StringComparer.Ordinal
        );
        if (attributes is not null)
        {
            foreach (KeyValuePair<string, string> attribute in attributes)
            {
                if (
                    string.IsNullOrWhiteSpace(attribute.Key)
                    || string.IsNullOrWhiteSpace(attribute.Value)
                )
                    continue;

                mergedAttributes[NormalizeTagKey(attribute.Key)] = NormalizeTagValue(
                    attribute.Value
                );
            }
        }

        return mergedAttributes;
    }

    private void ConfigureBaseScope(string telemetryInstallationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(telemetryInstallationId);

        _sentrySdk.ConfigureScope(scope =>
        {
            scope.User = new SentryUser { Id = telemetryInstallationId };

            foreach (KeyValuePair<string, string> tag in CreateBaseTags())
                scope.SetTag(tag.Key, tag.Value);
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

    private static IReadOnlyDictionary<string, string> CreateBaseTags()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["telemetry_schema_version"] = TelemetrySchemaVersion,
            ["os"] = AppRuntimeInfo.GetOperatingSystemTag(),
            ["os_arch"] = AppRuntimeInfo.GetProcessArchitectureTag(),
            ["session_type"] = AppRuntimeInfo.GetSessionTypeTag(),
            ["app_version"] = AppRuntimeInfo.GetInformationalVersion(),
            ["build_channel"] = GetSentryEnvironment(),
            ["distribution_channel"] = AppRuntimeInfo.GetDistributionChannel(),
            ["package_kind"] = AppRuntimeInfo.GetPackageKind(),
        };
    }

    private static string NormalizeTagValue(string value)
    {
        return AppRuntimeInfo.NormalizeTagValue(value);
    }

    private static string NormalizeTagKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
