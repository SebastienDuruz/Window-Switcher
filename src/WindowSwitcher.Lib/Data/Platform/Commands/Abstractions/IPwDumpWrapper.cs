namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface IPwDumpWrapper : ICommandWrapper
{
    /// <summary>
    /// Executes <c>pw-dump</c> with the provided timeout.
    /// </summary>
    /// <param name="timeoutMs">Maximum execution time in milliseconds.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    string Execute(int timeoutMs);

    /// <summary>
    /// Asynchronously executes <c>pw-dump</c> with the provided timeout.
    /// </summary>
    /// <param name="timeoutMs">Maximum execution time in milliseconds.</param>
    /// <param name="cancellationToken">Token used to cancel command execution.</param>
    /// <returns>The command output, or an empty string when execution fails.</returns>
    Task<string> ExecuteAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Execute(timeoutMs));
    }
}
