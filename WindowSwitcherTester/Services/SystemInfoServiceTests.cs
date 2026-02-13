using Microsoft.Extensions.Logging.Abstractions;
using WindowSwitcherLib.Application.Platform;
using WindowSwitcherLib.Application.Services;
using Xunit;

namespace WindowSwitcherTester.Services;

public sealed class SystemInfoServiceTests
{
    [Fact]
    public async Task GetCurrentUserAsync_ReturnsTrimmedValue_WhenRunnerSucceeds()
    {
        var commandRunner = new FakeCommandRunner(_ =>
            Task.FromResult(new CommandResult(0, "alice\n", string.Empty, TimedOut: false)));

        var sut = new SystemInfoService(commandRunner, NullLogger<SystemInfoService>.Instance);

        string user = await sut.GetCurrentUserAsync();

        Assert.Equal("alice", user);
        Assert.Single(commandRunner.Requests);
        Assert.Equal("whoami", commandRunner.Requests[0].ShellCommand);
    }

    [Fact]
    public async Task GetDotnetVersionAsync_ReturnsEmpty_WhenRunnerFails()
    {
        var commandRunner = new FakeCommandRunner(_ =>
            Task.FromResult(new CommandResult(127, string.Empty, "dotnet not found", TimedOut: false)));

        var sut = new SystemInfoService(commandRunner, NullLogger<SystemInfoService>.Instance);

        string dotnetVersion = await sut.GetDotnetVersionAsync();

        Assert.Equal(string.Empty, dotnetVersion);
        Assert.Single(commandRunner.Requests);
        Assert.Equal("dotnet", commandRunner.Requests[0].ExecutablePath);
    }

    private sealed class FakeCommandRunner(Func<CommandRequest, Task<CommandResult>> callback) : ICommandRunner
    {
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return callback(request);
        }
    }
}
