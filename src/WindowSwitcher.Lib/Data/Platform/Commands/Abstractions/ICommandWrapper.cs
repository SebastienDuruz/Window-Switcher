namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface ICommandWrapper
{
    /// <summary>
    /// Executes the command with the provided argument string.
    /// </summary>
    /// <param name="args">Command arguments.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    public string Execute(string args);

    /// <summary>
    /// Asynchronously executes the command with the provided argument string.
    /// </summary>
    /// <param name="args">Command arguments.</param>
    /// <param name="cancellationToken">Token used to cancel command execution.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    public Task<string> ExecuteAsync(string args, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Execute(args));
    }
}
