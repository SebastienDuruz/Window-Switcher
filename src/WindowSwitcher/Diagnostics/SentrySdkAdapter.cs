using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sentry;

namespace WindowSwitcher.Diagnostics;

internal interface ISentrySdkAdapter
{
    IDisposable Init(Action<SentryOptions> configureOptions);

    void ConfigureScope(Action<Scope> configureScope);

    void CaptureException(Exception exception, Action<Scope> configureScope);

    void EmitCounter(
        string name,
        double value,
        IReadOnlyDictionary<string, string>? attributes = null
    );

    Task FlushAsync(TimeSpan timeout);
}

internal sealed class SentrySdkAdapter : ISentrySdkAdapter
{
    public IDisposable Init(Action<SentryOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(configureOptions);
        return SentrySdk.Init(configureOptions);
    }

    public void ConfigureScope(Action<Scope> configureScope)
    {
        ArgumentNullException.ThrowIfNull(configureScope);
        SentrySdk.ConfigureScope(configureScope);
    }

    public void CaptureException(Exception exception, Action<Scope> configureScope)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(configureScope);
        SentrySdk.CaptureException(exception, configureScope);
    }

    public void EmitCounter(
        string name,
        double value,
        IReadOnlyDictionary<string, string>? attributes = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (attributes is null || attributes.Count == 0)
        {
            SentrySdk.Metrics.EmitCounter(name, value);
            return;
        }

        IEnumerable<KeyValuePair<string, object>> metricAttributes = attributes.Select(
            entry => new KeyValuePair<string, object>(entry.Key, entry.Value)
        );
        SentrySdk.Metrics.EmitCounter(name, value, metricAttributes);
    }

    public Task FlushAsync(TimeSpan timeout)
    {
        return SentrySdk.FlushAsync(timeout);
    }
}
