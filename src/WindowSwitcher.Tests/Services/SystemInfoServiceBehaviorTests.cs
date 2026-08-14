using WindowSwitcher.Lib.Data.Platform.SystemInfo;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class SystemInfoServiceBehaviorTests
{
    [Fact]
    public void Constructor_Throws_WhenCommandRunnerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemInfoService(commandRunner: null!));
    }

    [Fact]
    public async Task GetDotnetVersionAsync_BuildsExpectedCommandRequest()
    {
        var runner = new CapturingCommandRunner(
            (_, _) => Task.FromResult(new CommandResult(0, "10.0.100\n", string.Empty, false))
        );
        var sut = new SystemInfoService(runner);

        string version = await sut.GetDotnetVersionAsync();

        Assert.Equal("10.0.100", version);
        CommandRequest request = Assert.Single(runner.Requests);
        Assert.Equal("dotnet", request.ExecutablePath);
        Assert.Equal(new[] { "--version" }, request.Arguments);
        Assert.Null(request.ShellCommand);
        Assert.Equal(TimeSpan.FromSeconds(2), request.Timeout);
    }

    [Fact]
    public async Task GetCurrentUserAsync_PropagatesExternalCancellation()
    {
        var runner = new CapturingCommandRunner(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new CommandResult(0, "ignored", string.Empty, false));
            }
        );
        var sut = new SystemInfoService(runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.GetCurrentUserAsync(cts.Token)
        );
    }

    private sealed class CapturingCommandRunner(
        Func<CommandRequest, CancellationToken, Task<CommandResult>> callback
    ) : ICommandRunner
    {
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(
            CommandRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            return callback(request, cancellationToken);
        }
    }
}
