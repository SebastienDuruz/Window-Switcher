using WindowSwitcherLib.Data.Platform.Commands;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcherLib.Models;
using Xunit;

namespace WindowSwitcherTester.Services;

public sealed class PlatformCommandRunnerIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_WhoAmI_ReturnsOutput_OnSupportedPlatform()
    {
        ICommandRunner? runner = CreateRunnerForCurrentPlatform();
        if (runner is null)
            return;

        CommandResult result = await runner.RunAsync(
            CommandRequest.ForShell("whoami") with { Timeout = TimeSpan.FromSeconds(5) });

        Assert.True(result.IsSuccess, $"ExitCode={result.ExitCode}, TimedOut={result.TimedOut}, Stderr={result.StandardError}");
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    private static ICommandRunner? CreateRunnerForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsCommandRunner();

        if (OperatingSystem.IsLinux())
            return new LinuxCommandRunner();

        return null;
    }
}
