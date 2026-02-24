using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

/// <summary>
/// Executes system commands behind an application port.
/// </summary>
public interface ICommandRunner
{
    /// <summary>
    /// Executes a command request.
    /// </summary>
    Task<CommandResult> RunAsync(
        CommandRequest request,
        CancellationToken cancellationToken = default
    );
}
