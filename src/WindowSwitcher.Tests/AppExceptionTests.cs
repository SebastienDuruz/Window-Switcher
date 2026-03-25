using Sentry;
using WindowSwitcher;
using Xunit;

namespace WindowSwitcher.Tests
{
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

        [Fact]
        public void GetSentryRelease_UsesApplicationVersion()
        {
            string release = App.GetSentryRelease();

            Assert.Equal("window-switcher@0.9.0", release);
        }

        [Fact]
        public void FilterSentryEvent_ReturnsNull_ForOperationCanceledException()
        {
            var sentryEvent = new SentryEvent(new TaskCanceledException("cancelled"));

            SentryEvent? filtered = App.FilterSentryEvent(sentryEvent);

            Assert.Null(filtered);
        }

        [Fact]
        public void ShouldDropExceptionFromSentry_ReturnsTrue_WhenAllTerminalExceptionsAreIgnorable()
        {
            var exception = new AggregateException(
                new TaskCanceledException("cancelled"),
                new Tmds.DBus.FakeDisconnectedException()
            );

            bool shouldDrop = App.ShouldDropExceptionFromSentry(exception);

            Assert.True(shouldDrop);
        }

        [Fact]
        public void ShouldDropExceptionFromSentry_ReturnsFalse_WhenAnyTerminalExceptionIsUnexpected()
        {
            var exception = new AggregateException(
                new TaskCanceledException("cancelled"),
                new InvalidOperationException("boom")
            );

            bool shouldDrop = App.ShouldDropExceptionFromSentry(exception);

            Assert.False(shouldDrop);
        }
    }
}

namespace Tmds.DBus
{
    public sealed class FakeDisconnectedException : Exception;
}
