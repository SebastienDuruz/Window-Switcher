namespace WindowSwitcherLib.Models;

/// <summary>
/// Contains process execution outcome.
/// </summary>
public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    /// <summary>
    /// Indicates whether command succeeded and did not time out.
    /// </summary>
    public bool IsSuccess => !TimedOut && ExitCode == 0;
}
