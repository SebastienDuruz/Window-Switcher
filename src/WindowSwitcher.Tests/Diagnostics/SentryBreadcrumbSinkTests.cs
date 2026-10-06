using Serilog.Events;
using Serilog.Parsing;
using WindowSwitcher.Diagnostics;
using Xunit;

namespace WindowSwitcher.Tests.Diagnostics;

public sealed class SentryBreadcrumbSinkTests
{
    [Fact]
    public void Emit_SendsTemplateTextWithoutPropertyValues()
    {
        var sentrySdk = new RecordingSentrySdkAdapter();
        var sut = new SentryBreadcrumbSink(sentrySdk);

        sut.Emit(
            CreateEvent(
                LogEventLevel.Warning,
                "Reading {DevicePath} was refused",
                properties: [new LogEventProperty("DevicePath", new ScalarValue("/home/secret"))]
            )
        );

        RecordedBreadcrumb breadcrumb = Assert.Single(sentrySdk.Breadcrumbs);
        Assert.Equal("Reading {DevicePath} was refused", breadcrumb.Message);
        Assert.DoesNotContain("/home/secret", breadcrumb.Message, StringComparison.Ordinal);
        Assert.Null(breadcrumb.Data);
        Assert.Empty(sentrySdk.CapturedExceptions);
    }

    [Fact]
    public void Emit_UsesSourceContextAsCategory()
    {
        var sentrySdk = new RecordingSentrySdkAdapter();
        var sut = new SentryBreadcrumbSink(sentrySdk);

        sut.Emit(
            CreateEvent(
                LogEventLevel.Information,
                "started",
                properties:
                [
                    new LogEventProperty(
                        Serilog.Core.Constants.SourceContextPropertyName,
                        new ScalarValue("WindowSwitcher.App")
                    ),
                ]
            )
        );
        sut.Emit(CreateEvent(LogEventLevel.Information, "no context"));

        Assert.Equal("WindowSwitcher.App", sentrySdk.Breadcrumbs[0].Category);
        Assert.Equal(SentryBreadcrumbSink.DefaultCategory, sentrySdk.Breadcrumbs[1].Category);
    }

    [Fact]
    public void Emit_AddsOnlyExceptionTypeAsData()
    {
        var sentrySdk = new RecordingSentrySdkAdapter();
        var sut = new SentryBreadcrumbSink(sentrySdk);

        sut.Emit(
            CreateEvent(
                LogEventLevel.Error,
                "capture failed",
                new InvalidOperationException("contains /home/secret")
            )
        );

        RecordedBreadcrumb breadcrumb = Assert.Single(sentrySdk.Breadcrumbs);
        Assert.NotNull(breadcrumb.Data);
        KeyValuePair<string, string> entry = Assert.Single(breadcrumb.Data);
        Assert.Equal(SentryBreadcrumbSink.ExceptionTypeDataKey, entry.Key);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.Value);
        Assert.Empty(sentrySdk.CapturedExceptions);
    }

    [Theory]
    [InlineData(LogEventLevel.Debug, BreadcrumbLevel.Debug)]
    [InlineData(LogEventLevel.Information, BreadcrumbLevel.Info)]
    [InlineData(LogEventLevel.Warning, BreadcrumbLevel.Warning)]
    [InlineData(LogEventLevel.Error, BreadcrumbLevel.Error)]
    [InlineData(LogEventLevel.Fatal, BreadcrumbLevel.Fatal)]
    public void MapLevel_MapsSerilogLevels(LogEventLevel level, BreadcrumbLevel expected)
    {
        Assert.Equal(expected, SentryBreadcrumbSink.MapLevel(level));
    }

    [Fact]
    public void Emit_SwallowsAdapterFailures()
    {
        var sut = new SentryBreadcrumbSink(
            new RecordingSentrySdkAdapter { ThrowOnBreadcrumb = true }
        );

        Exception? exception = Record.Exception(() =>
            sut.Emit(CreateEvent(LogEventLevel.Error, "boom"))
        );

        Assert.Null(exception);
    }

    private static LogEvent CreateEvent(
        LogEventLevel level,
        string template,
        Exception? exception = null,
        IEnumerable<LogEventProperty>? properties = null
    )
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            new MessageTemplateParser().Parse(template),
            properties ?? []
        );
    }

    private sealed record RecordedBreadcrumb(
        string Message,
        string? Category,
        BreadcrumbLevel Level,
        IDictionary<string, string>? Data
    );

    private sealed class RecordingSentrySdkAdapter : ISentrySdkAdapter
    {
        public bool ThrowOnBreadcrumb { get; init; }
        public List<RecordedBreadcrumb> Breadcrumbs { get; } = [];
        public List<Exception> CapturedExceptions { get; } = [];

        public IDisposable Init(Action<SentryOptions> configureOptions) =>
            throw new NotSupportedException();

        public void ConfigureScope(Action<Scope> configureScope) { }

        public void CaptureException(Exception exception, Action<Scope> configureScope)
        {
            CapturedExceptions.Add(exception);
        }

        public void AddBreadcrumb(
            string message,
            string? category,
            BreadcrumbLevel level,
            IDictionary<string, string>? data = null
        )
        {
            if (ThrowOnBreadcrumb)
                throw new InvalidOperationException("adapter failure");

            Breadcrumbs.Add(new RecordedBreadcrumb(message, category, level, data));
        }

        public void EmitCounter(
            string name,
            double value,
            IReadOnlyDictionary<string, string>? attributes = null
        ) { }

        public Task FlushAsync(TimeSpan timeout) => Task.CompletedTask;
    }
}
