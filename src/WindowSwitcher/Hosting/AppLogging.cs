using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using WindowSwitcher.Diagnostics;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Diagnostics.Logging;

namespace WindowSwitcher.Hosting;

/// <summary>
/// Configures the process-wide Serilog logger. Must run first in <c>Program.Main</c> so that
/// every <c>Log.ForContext</c> call made afterwards binds to the configured pipeline.
/// </summary>
internal static class AppLogging
{
    internal const string LogFileName = "window-switcher-.log";
    internal const int RetainedFileCountLimit = 7;
    internal const long FileSizeLimitBytes = 10L * 1024 * 1024;
    internal const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}";

    private const int AsyncBufferSize = 10_000;

    public static void Initialize()
    {
        LoggerConfiguration configuration = new LoggerConfiguration()
            .MinimumLevel.Is(GetMinimumLevel())
            .Enrich.FromLogContext()
            .Filter.With(new RepeatedLogEventFilter())
            .WriteTo.Async(
                sink =>
                    sink.File(
                        Path.Combine(StaticData.LogFolder, LogFileName),
                        outputTemplate: OutputTemplate,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: RetainedFileCountLimit,
                        fileSizeLimitBytes: FileSizeLimitBytes,
                        rollOnFileSizeLimit: true
                    ),
                bufferSize: AsyncBufferSize,
                // Drop lines rather than block the UI thread when the disk is slow.
                blockWhenFull: false
            );
#if SENTRY_TELEMETRY
        configuration = configuration.WriteTo.Sink(
            new SentryBreadcrumbSink(new SentrySdkAdapter()),
            LogEventLevel.Information
        );
#endif

        Log.Logger = configuration.CreateLogger();
        Log.ForContext(typeof(AppLogging))
            .Information(
                "Window Switcher {Version} starting ({OperatingSystem}/{Architecture}, session {SessionType}, {BuildChannel}, {DistributionChannel}/{PackageKind})",
                AppRuntimeInfo.GetInformationalVersion(),
                AppRuntimeInfo.GetOperatingSystemTag(),
                AppRuntimeInfo.GetProcessArchitectureTag(),
                AppRuntimeInfo.GetSessionTypeTag(),
                AppRuntimeInfo.GetBuildChannel(),
                AppRuntimeInfo.GetDistributionChannel(),
                AppRuntimeInfo.GetPackageKind()
            );
    }

    public static ValueTask CloseAndFlushAsync()
    {
        return Log.CloseAndFlushAsync();
    }

    private static LogEventLevel GetMinimumLevel()
    {
#if DEBUG
        return LogEventLevel.Debug;
#else
        return LogEventLevel.Information;
#endif
    }
}
