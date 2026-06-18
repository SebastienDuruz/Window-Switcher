namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface IGdbusWrapper : ICommandWrapper
{
    /// <summary>
    /// Executes <c>gdbus</c> with an argument list and timeout.
    /// </summary>
    /// <param name="args">Command arguments.</param>
    /// <param name="timeoutMs">Maximum execution time in milliseconds.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    string Execute(IReadOnlyList<string> args, int timeoutMs);

    /// <summary>
    /// Asynchronously executes <c>gdbus</c> with an argument list and timeout.
    /// </summary>
    /// <param name="args">Command arguments.</param>
    /// <param name="timeoutMs">Maximum execution time in milliseconds.</param>
    /// <param name="cancellationToken">Token used to cancel command execution.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    Task<string> ExecuteAsync(
        IReadOnlyList<string> args,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(Execute(args, timeoutMs));
    }
}
