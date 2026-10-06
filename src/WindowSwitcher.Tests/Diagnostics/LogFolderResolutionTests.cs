using WindowSwitcher.Lib.Data;
using Xunit;

namespace WindowSwitcher.Tests.Diagnostics;

public sealed class LogFolderResolutionTests
{
    private const string UserHome = "/home/tester";
    private const string LocalApplicationData = "/unused/local";

    [Fact]
    public void ResolveLogFolder_UsesXdgStateHomeWhenAbsolute()
    {
        string folder = Resolve(xdgStateHome: "/var/state/tester", isWindows: false);

        Assert.Equal(Path.Combine("/var/state/tester", StaticData.AppName, "logs"), folder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/state")]
    public void ResolveLogFolder_FallsBackToLocalStateWhenXdgStateHomeIsMissingOrInvalid(
        string? xdgStateHome
    )
    {
        string folder = Resolve(xdgStateHome, isWindows: false);

        Assert.Equal(Path.Combine(UserHome, ".local", "state", StaticData.AppName, "logs"), folder);
    }

    [Fact]
    public void ResolveLogFolder_UsesLocalApplicationDataOnWindows()
    {
        string folder = Resolve(xdgStateHome: "/ignored/on/windows", isWindows: true);

        Assert.Equal(Path.Combine(LocalApplicationData, StaticData.AppName, "logs"), folder);
    }

    private static string Resolve(string? xdgStateHome, bool isWindows)
    {
        return StaticData.ResolveLogFolder(
            name => name == "XDG_STATE_HOME" ? xdgStateHome : null,
            isWindows,
            UserHome,
            LocalApplicationData
        );
    }
}
