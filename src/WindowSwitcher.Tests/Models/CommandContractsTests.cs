using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Models;

public sealed class CommandContractsTests
{
    [Fact]
    public void ForExecutable_CreatesRequestWithExecutableAndArguments()
    {
        CommandRequest request = CommandRequest.ForExecutable("dotnet", "--version", "--info");

        Assert.Equal("dotnet", request.ExecutablePath);
        Assert.Equal(new[] { "--version", "--info" }, request.Arguments);
        Assert.Null(request.ShellCommand);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
        Assert.Empty(request.EnvironmentVariables);
    }

    [Fact]
    public void ForShell_CreatesRequestWithShellCommand()
    {
        CommandRequest request = CommandRequest.ForShell("whoami");

        Assert.Null(request.ExecutablePath);
        Assert.Equal("whoami", request.ShellCommand);
        Assert.Empty(request.Arguments);
    }

    [Fact]
    public void ForExecutable_Throws_WhenExecutableIsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => CommandRequest.ForExecutable("   ", "--version"));
    }

    [Fact]
    public void ForShell_Throws_WhenShellCommandIsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => CommandRequest.ForShell(" "));
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(-1, false, false)]
    public void CommandResult_IsSuccess_MatchesExitCodeAndTimeout(
        int exitCode,
        bool timedOut,
        bool expected
    )
    {
        var result = new CommandResult(exitCode, string.Empty, string.Empty, timedOut);

        Assert.Equal(expected, result.IsSuccess);
    }
}
