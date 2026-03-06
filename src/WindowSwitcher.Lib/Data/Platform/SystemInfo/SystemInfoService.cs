using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.SystemInfo;

/// <summary>
/// Application service consuming <see cref="ICommandRunner"/> without OS branches.
/// </summary>
public sealed class SystemInfoService
{
    private readonly ICommandRunner _commandRunner;

    public SystemInfoService(ICommandRunner commandRunner)
    {
        ArgumentNullException.ThrowIfNull(commandRunner);

        _commandRunner = commandRunner;
    }

    /// <summary>
    /// Returns the current user using a shell command supported by both adapters.
    /// </summary>
    public async Task<string> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await _commandRunner
            .RunAsync(
                CommandRequest.ForShell("whoami") with
                {
                    Timeout = TimeSpan.FromSeconds(2),
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!result.IsSuccess)
            return string.Empty;

        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Returns installed .NET SDK runtime version when available.
    /// </summary>
    public async Task<string> GetDotnetVersionAsync(CancellationToken cancellationToken = default)
    {
        CommandResult result = await _commandRunner
            .RunAsync(
                CommandRequest.ForExecutable("dotnet", "--version") with
                {
                    Timeout = TimeSpan.FromSeconds(2),
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!result.IsSuccess)
            return string.Empty;

        return result.StandardOutput.Trim();
    }
}
