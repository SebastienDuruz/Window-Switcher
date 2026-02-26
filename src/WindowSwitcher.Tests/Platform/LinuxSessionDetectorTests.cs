using WindowSwitcherLib.Data.Platform.Commands.Dependencies;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxSessionDetectorTests
{
    [Fact]
    public void GetSessionType_ReturnsNormalizedEnvironmentValue_OnLinux()
    {
        string? previous = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", " WayLand ");

            string? sessionType = LinuxSessionDetector.GetSessionType();

            if (OperatingSystem.IsLinux())
                Assert.Equal("wayland", sessionType);
            else
                Assert.Null(sessionType);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", previous);
        }
    }

    [Fact]
    public void GetSessionType_ReturnsNull_WhenEnvironmentValueIsWhitespace()
    {
        string? previous = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "   ");

            string? sessionType = LinuxSessionDetector.GetSessionType();

            Assert.Null(sessionType);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", previous);
        }
    }
}
