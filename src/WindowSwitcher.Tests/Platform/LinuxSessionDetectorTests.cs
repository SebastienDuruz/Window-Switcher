using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxSessionDetectorTests
{
    [Theory]
    [InlineData("x11", null, null, LinuxSessionKind.X11)]
    [InlineData(" WayLand ", null, null, LinuxSessionKind.Wayland)]
    [InlineData("unknown", "wayland-0", ":1", LinuxSessionKind.Wayland)]
    [InlineData(null, "wayland-0", ":1", LinuxSessionKind.Wayland)]
    [InlineData(null, null, ":1", LinuxSessionKind.X11)]
    [InlineData("unknown", null, null, LinuxSessionKind.Unsupported)]
    public void Detect_UsesDeclaredSessionThenDisplayVariables(
        string? sessionType,
        string? waylandDisplay,
        string? xDisplay,
        LinuxSessionKind expected
    )
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = sessionType,
            ["WAYLAND_DISPLAY"] = waylandDisplay,
            ["DISPLAY"] = xDisplay,
        };

        LinuxSessionKind actual = LinuxSessionDetector.Detect(
            variable => environment.GetValueOrDefault(variable),
            isLinux: true
        );

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Detect_ReturnsUnsupportedOutsideLinux()
    {
        LinuxSessionKind actual = LinuxSessionDetector.Detect(
            _ => throw new InvalidOperationException(),
            isLinux: false
        );

        Assert.Equal(LinuxSessionKind.Unsupported, actual);
    }

    [Theory]
    [InlineData("KDE", LinuxDesktopKind.Kde)]
    [InlineData(" kde ", LinuxDesktopKind.Kde)]
    [InlineData("custom:KDE", LinuxDesktopKind.Kde)]
    [InlineData("ubuntu:GNOME", LinuxDesktopKind.Other)]
    [InlineData("KDEX", LinuxDesktopKind.Other)]
    [InlineData(null, LinuxDesktopKind.Other)]
    public void DetectDesktop_RecognizesKdeInCurrentDesktopList(
        string? currentDesktop,
        LinuxDesktopKind expected
    )
    {
        LinuxDesktopKind actual = LinuxSessionDetector.DetectDesktop(
            variable => variable == "XDG_CURRENT_DESKTOP" ? currentDesktop : null,
            isLinux: true
        );

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DetectDesktop_ReturnsOtherOutsideLinux()
    {
        LinuxDesktopKind actual = LinuxSessionDetector.DetectDesktop(
            _ => throw new InvalidOperationException(),
            isLinux: false
        );

        Assert.Equal(LinuxDesktopKind.Other, actual);
    }
}
