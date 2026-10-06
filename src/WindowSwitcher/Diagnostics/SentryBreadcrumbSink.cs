using System;
using System.Collections.Generic;
using Sentry;
using Serilog.Core;
using Serilog.Events;

namespace WindowSwitcher.Diagnostics;

/// <summary>
/// Forwards log events to Sentry as breadcrumbs only; it never creates Sentry events.
/// The breadcrumb carries the message template text, never the rendered message, so
/// property values (paths, identifiers, window data) are not sent.
/// </summary>
internal sealed class SentryBreadcrumbSink : ILogEventSink
{
    internal const string DefaultCategory = "log";
    internal const string ExceptionTypeDataKey = "exception_type";

    private readonly ISentrySdkAdapter _sentrySdk;

    public SentryBreadcrumbSink(ISentrySdkAdapter sentrySdk)
    {
        ArgumentNullException.ThrowIfNull(sentrySdk);
        _sentrySdk = sentrySdk;
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        Dictionary<string, string>? data = null;
        if (logEvent.Exception is not null)
        {
            Type exceptionType = logEvent.Exception.GetType();
            data = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ExceptionTypeDataKey] = exceptionType.FullName ?? exceptionType.Name,
            };
        }

        try
        {
            _sentrySdk.AddBreadcrumb(
                logEvent.MessageTemplate.Text,
                GetCategory(logEvent),
                MapLevel(logEvent.Level),
                data
            );
        }
        catch (Exception)
        {
            // Telemetry must never affect the application, and logging here would recurse.
        }
    }

    internal static BreadcrumbLevel MapLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose or LogEventLevel.Debug => BreadcrumbLevel.Debug,
            LogEventLevel.Information => BreadcrumbLevel.Info,
            LogEventLevel.Warning => BreadcrumbLevel.Warning,
            LogEventLevel.Error => BreadcrumbLevel.Error,
            _ => BreadcrumbLevel.Fatal,
        };
    }

    private static string GetCategory(LogEvent logEvent)
    {
        return
            logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            && value is ScalarValue { Value: string sourceContext }
            && !string.IsNullOrWhiteSpace(sourceContext)
            ? sourceContext
            : DefaultCategory;
    }
}
