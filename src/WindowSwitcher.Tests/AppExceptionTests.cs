using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sentry;
using Tmds.DBus;
using WindowSwitcher.Diagnostics;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests;

public sealed class SentryAppTelemetryTests
{
    private const string ValidDsn = "https://examplePublicKey@o0.ingest.sentry.io/0";
    private const string ValidTelemetryUserId = "e084ab00-8f33-4ad4-955a-bbc615590c6e";
    private static readonly string DefaultDsn = new ConfigFile().SentryDsn;
    public static TheoryData<string?, string> ResolveSentryDsnCases =>
        new()
        {
            { ValidDsn, ValidDsn },
            {
                "  https://customPublicKey@o0.ingest.sentry.io/1  ",
                "https://customPublicKey@o0.ingest.sentry.io/1"
            },
            { string.Empty, DefaultDsn },
            { "   ", DefaultDsn },
            { null, DefaultDsn }
        };
    public static TheoryData<string?, string> ResolveTelemetryUserIdExplicitCases =>
        new()
        {
            { ValidTelemetryUserId, ValidTelemetryUserId },
            { "  e084ab00-8f33-4ad4-955a-bbc615590c6e  ", ValidTelemetryUserId }
        };

    [Fact]
    public void CreateUnhandledException_ReturnsOriginalException_WhenPayloadIsException()
    {
        var exception = new InvalidOperationException("boom");

        Exception result = SentryAppTelemetry.CreateUnhandledException(exception);

        Assert.Same(exception, result);
    }

    [Fact]
    public void CreateUnhandledException_WrapsNullPayload()
    {
        Exception result = SentryAppTelemetry.CreateUnhandledException(null);

        InvalidOperationException wrapped = Assert.IsType<InvalidOperationException>(result);
        Assert.Contains("null", wrapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUnhandledException_WrapsNonExceptionPayload()
    {
        Exception result = SentryAppTelemetry.CreateUnhandledException("boom");

        InvalidOperationException wrapped = Assert.IsType<InvalidOperationException>(result);
        Assert.Contains("System.String", wrapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSentryRelease_UsesApplicationVersion()
    {
        string release = SentryAppTelemetry.GetSentryRelease();

        Assert.Equal("window-switcher@0.9.0", release);
    }

    [Fact]
    public void FilterSentryEvent_ReturnsNull_ForOperationCanceledException()
    {
        var sentryEvent = new SentryEvent(new TaskCanceledException("cancelled"));

        SentryEvent? filtered = SentryAppTelemetry.FilterSentryEvent(sentryEvent);

        Assert.Null(filtered);
    }

    [Fact]
    public void ShouldDropExceptionFromSentry_ReturnsTrue_WhenAllTerminalExceptionsAreIgnorable()
    {
        var exception = new AggregateException(
            new TaskCanceledException("cancelled"),
            new FakeDisconnectedException()
        );

        bool shouldDrop = SentryAppTelemetry.ShouldDropExceptionFromSentry(exception);

        Assert.True(shouldDrop);
    }

    [Fact]
    public void ShouldDropExceptionFromSentry_ReturnsFalse_WhenAnyTerminalExceptionIsUnexpected()
    {
        var exception = new AggregateException(
            new TaskCanceledException("cancelled"),
            new InvalidOperationException("boom")
        );

        bool shouldDrop = SentryAppTelemetry.ShouldDropExceptionFromSentry(exception);

        Assert.False(shouldDrop);
    }

    [Theory]
    [InlineData("PipeWire", "pipewire")]
    [InlineData("Screenshots", "screenshots")]
    [InlineData("Desktop Window Manager (DWM)", "dwm")]
    [InlineData("SomethingElse", "other")]
    [InlineData(null, "unknown")]
    public void NormalizePreviewMode_ReturnsExpectedValue(string? previewMode, string expected)
    {
        string normalized = SentryAppTelemetry.NormalizePreviewMode(previewMode);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [MemberData(nameof(ResolveSentryDsnCases))]
    public void ResolveSentryDsn_ReturnsConfiguredValueOrFallback(string? dsn, string expected)
    {
        string resolvedDsn = SentryAppTelemetry.ResolveSentryDsn(dsn);

        Assert.Equal(expected, resolvedDsn);
    }

    [Theory]
    [MemberData(nameof(ResolveTelemetryUserIdExplicitCases))]
    public void ResolveTelemetryUserId_ReturnsConfiguredValueOrFallback(
        string? telemetryUserId,
        string expected
    )
    {
        string resolvedTelemetryUserId = SentryAppTelemetry.ResolveTelemetryUserId(telemetryUserId);

        Assert.Equal(expected, resolvedTelemetryUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData(null)]
    public void ResolveTelemetryUserId_ReturnsValidGuid_WhenFallbackIsUsed(string? telemetryUserId)
    {
        string resolvedTelemetryUserId = SentryAppTelemetry.ResolveTelemetryUserId(telemetryUserId);

        Assert.True(Guid.TryParse(resolvedTelemetryUserId, out _));
    }

    [Fact]
    public void Initialize_ConfiguresSdkAndBaseScope_WhenEnabled()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateSut(
            sentrySdk,
            new FakeTelemetrySettingsProvider(true, ValidDsn, ValidTelemetryUserId)
        );

        sut.Initialize();

        Assert.Equal(1, sentrySdk.InitCallCount);
        Assert.NotNull(sentrySdk.LastOptions);
        Assert.Equal(ValidDsn, sentrySdk.LastOptions!.Dsn);
        Assert.Equal("window-switcher@0.9.0", sentrySdk.LastOptions.Release);
        Assert.Equal(SentryAppTelemetry.GetSentryEnvironment(), sentrySdk.LastOptions.Environment);
        Assert.False(sentrySdk.LastOptions.SendDefaultPii);
        Assert.True(sentrySdk.ScopeTags.ContainsKey("os"));
        Assert.True(sentrySdk.ScopeTags.ContainsKey("session_type"));
        Assert.Equal("0.9.0", sentrySdk.ScopeTags["app_version"]);
        Assert.Equal(SentryAppTelemetry.GetSentryEnvironment(), sentrySdk.ScopeTags["build_channel"]);
        Assert.Equal(ValidTelemetryUserId, sentrySdk.ScopeTags["telemetry_user_id"]);
        Assert.Equal(ValidTelemetryUserId, sentrySdk.ScopeUserId);
    }

    [Fact]
    public void Initialize_SkipsSdkInitialization_WhenDisabled()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateSut(
            sentrySdk,
            new FakeTelemetrySettingsProvider(false, ValidDsn, ValidTelemetryUserId)
        );

        sut.Initialize();

        Assert.Equal(0, sentrySdk.InitCallCount);
    }

    [Fact]
    public void Initialize_AttemptsSdkInitialization_WithoutPrevalidatingDsn()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateSut(
            sentrySdk,
            new FakeTelemetrySettingsProvider(true, "not-a-dsn", ValidTelemetryUserId)
        );

        sut.Initialize();

        Assert.Equal(1, sentrySdk.InitCallCount);
        Assert.Equal("not-a-dsn", sentrySdk.LastOptions!.Dsn);
    }

    [Fact]
    public async Task RecordAppStartedAsync_EmitsSingleCounterMetric()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateInitializedSut(sentrySdk);

        await sut.RecordAppStartedAsync("PipeWire");

        CapturedMetricRecord metric = Assert.Single(sentrySdk.CapturedMetrics);
        Assert.Equal(SentryAppTelemetry.AppStartedMetricName, metric.Name);
        Assert.Equal(1d, metric.Value);
        Assert.Equal("pipewire", metric.Attributes!["preview_mode"]);
        Assert.Equal("linux", metric.Attributes["os"]);
        Assert.Equal("0.9.0", metric.Attributes["app_version"]);
        Assert.Equal(ValidTelemetryUserId, metric.Attributes["telemetry_user_id"]);
        Assert.Equal(1, sentrySdk.FlushCallCount);
    }

    [Fact]
    public async Task RecordAppStartedAsync_DeduplicatesWithinSession()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateInitializedSut(sentrySdk);

        await sut.RecordAppStartedAsync("pipewire");
        await sut.RecordAppStartedAsync("pipewire");

        Assert.Single(sentrySdk.CapturedMetrics);
        Assert.Equal(1, sentrySdk.FlushCallCount);
    }

    [Fact]
    public void CreateAppStartedMetricAttributes_ReturnsExpectedEnvironmentData()
    {
        var attributes = SentryAppTelemetry.CreateAppStartedMetricAttributes(
            "PipeWire",
            ValidTelemetryUserId
        );

        Assert.Equal("linux", attributes["os"]);
        Assert.Equal("pipewire", attributes["preview_mode"]);
        Assert.Equal("0.9.0", attributes["app_version"]);
        Assert.Equal(SentryAppTelemetry.GetSentryEnvironment(), attributes["build_channel"]);
        Assert.Equal(ValidTelemetryUserId, attributes["telemetry_user_id"]);
        Assert.True(attributes.ContainsKey("session_type"));
    }

    [Fact]
    public void CaptureUnhandledException_SetsCaptureSourceTag()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateInitializedSut(sentrySdk);

        sut.CaptureUnhandledException(new InvalidOperationException("boom"), "dispatcher_unhandled");

        CapturedExceptionRecord capturedException = Assert.Single(sentrySdk.CapturedExceptions);
        Assert.Equal("dispatcher_unhandled", capturedException.Tags["capture_source"]);
    }

    [Fact]
    public async Task ShutdownAsync_FlushesAndDisposesSdkHandle()
    {
        var sentrySdk = new FakeSentrySdkAdapter();
        var sut = CreateInitializedSut(sentrySdk);

        await sut.ShutdownAsync();

        Assert.Equal(1, sentrySdk.FlushCallCount);
        Assert.True(sentrySdk.HandleDisposed);
    }

    private static SentryAppTelemetry CreateInitializedSut(
        FakeSentrySdkAdapter sentrySdk)
    {
        var sut = CreateSut(
            sentrySdk,
            new FakeTelemetrySettingsProvider(true, ValidDsn, ValidTelemetryUserId)
        );
        sut.Initialize();
        sentrySdk.ClearCapturedTelemetry();
        return sut;
    }

    private static SentryAppTelemetry CreateSut(
        FakeSentrySdkAdapter sentrySdk,
        ITelemetrySettingsProvider telemetrySettingsProvider)
    {
        return new SentryAppTelemetry(sentrySdk, telemetrySettingsProvider);
    }

    private sealed class FakeTelemetrySettingsProvider : ITelemetrySettingsProvider
    {
        private readonly bool _enableSentry;
        private readonly string _sentryDsn;
        private readonly string _telemetryUserId;

        public FakeTelemetrySettingsProvider(
            bool enableSentry,
            string sentryDsn,
            string telemetryUserId
        )
        {
            _enableSentry = enableSentry;
            _sentryDsn = sentryDsn;
            _telemetryUserId = telemetryUserId;
        }

        public (bool EnableSentry, string SentryDsn, string TelemetryUserId) GetSettings()
        {
            return (_enableSentry, _sentryDsn, _telemetryUserId);
        }
    }

    private sealed class FakeSentrySdkAdapter : ISentrySdkAdapter
    {
        public int InitCallCount { get; private set; }
        public int FlushCallCount { get; private set; }
        public bool HandleDisposed { get; private set; }
        public SentryOptions? LastOptions { get; private set; }
        public Dictionary<string, string> ScopeTags { get; } = new(StringComparer.Ordinal);
        public string ScopeUserId { get; private set; } = string.Empty;
        public List<CapturedMetricRecord> CapturedMetrics { get; } = [];
        public List<CapturedExceptionRecord> CapturedExceptions { get; } = [];

        public IDisposable Init(Action<SentryOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            InitCallCount++;
            LastOptions = new SentryOptions();
            configureOptions(LastOptions);
            return new DelegateDisposable(() => HandleDisposed = true);
        }

        public void ConfigureScope(Action<Scope> configureScope)
        {
            ArgumentNullException.ThrowIfNull(configureScope);

            var scope = new Scope(new SentryOptions());
            configureScope(scope);

            foreach ((string key, string value) in scope.Tags)
                ScopeTags[key] = value;
            ScopeUserId = scope.User?.Id ?? string.Empty;
        }

        public void CaptureException(Exception exception, Action<Scope> configureScope)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ArgumentNullException.ThrowIfNull(configureScope);

            var scope = new Scope(new SentryOptions());
            configureScope(scope);
            CapturedExceptions.Add(
                new CapturedExceptionRecord(
                    exception,
                    new Dictionary<string, string>(scope.Tags, StringComparer.Ordinal)
                )
            );
        }

        public void EmitCounter(
            string name,
            double value,
            IReadOnlyDictionary<string, string>? attributes = null
        )
        {
            CapturedMetrics.Add(
                new CapturedMetricRecord(
                    name,
                    value,
                    attributes is null
                        ? null
                        : new Dictionary<string, string>(attributes, StringComparer.Ordinal)
                )
            );
        }

        public Task FlushAsync(TimeSpan timeout)
        {
            FlushCallCount++;
            return Task.CompletedTask;
        }

        public void ClearCapturedTelemetry()
        {
            CapturedMetrics.Clear();
            CapturedExceptions.Clear();
            FlushCallCount = 0;
        }
    }

    private sealed class DelegateDisposable : IDisposable
    {
        private readonly Action _disposeAction;
        private bool _isDisposed;

        public DelegateDisposable(Action disposeAction)
        {
            ArgumentNullException.ThrowIfNull(disposeAction);
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _disposeAction();
        }
    }

    private sealed record CapturedMetricRecord(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string>? Attributes
    );

    private sealed record CapturedExceptionRecord(
        Exception Exception,
        IReadOnlyDictionary<string, string> Tags
    );
}
