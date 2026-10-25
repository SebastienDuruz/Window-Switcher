using Serilog;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

/// <summary>
/// Reads Linux process names from <c>/proc</c>.
/// </summary>
internal static class LinuxProcessNameReader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(LinuxProcessNameReader));

    /// <summary>
    /// Reads the short command name of a process.
    /// </summary>
    /// <returns>The process name, or an empty string when it cannot be read.</returns>
    public static async Task<string> ReadAsync(int processId, CancellationToken cancellationToken)
    {
        if (processId <= 0)
            return string.Empty;

        try
        {
            string processName = await File.ReadAllTextAsync(
                    $"/proc/{processId}/comm",
                    cancellationToken
                )
                .ConfigureAwait(false);
            return processName.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The process may have exited or be owned by another user: the name stays empty.
            Logger.Debug(
                exception,
                "Cannot read process name from /proc for PID {ProcessId}",
                processId
            );
            return string.Empty;
        }
    }
}
