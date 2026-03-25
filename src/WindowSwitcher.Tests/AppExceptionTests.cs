using WindowSwitcher;
using Xunit;

namespace WindowSwitcher.Tests;

public sealed class AppExceptionTests
{
    [Fact]
    public void CreateUnhandledException_ReturnsOriginalException_WhenPayloadIsException()
    {
        var exception = new InvalidOperationException("boom");

        Exception result = App.CreateUnhandledException(exception);

        Assert.Same(exception, result);
    }

    [Fact]
    public void CreateUnhandledException_WrapsNullPayload()
    {
        Exception result = App.CreateUnhandledException(null);

        InvalidOperationException wrapped = Assert.IsType<InvalidOperationException>(result);
        Assert.Contains("null", wrapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUnhandledException_WrapsNonExceptionPayload()
    {
        Exception result = App.CreateUnhandledException("boom");

        InvalidOperationException wrapped = Assert.IsType<InvalidOperationException>(result);
        Assert.Contains("System.String", wrapped.Message, StringComparison.Ordinal);
    }
}
