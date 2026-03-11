using System.Diagnostics;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

internal static class ProcessExecution
{
    public static async Task<CommandResult> RunAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start process '{process.StartInfo.FileName}'."
            );
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKillProcess(process);
            await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            await WaitForExitAfterKillAsync(process).ConfigureAwait(false);
            throw;
        }

        string standardOutput = await standardOutputTask.ConfigureAwait(false);
        string standardError = await standardErrorTask.ConfigureAwait(false);

        return new CommandResult(
            timedOut ? -1 : process.ExitCode,
            standardOutput,
            standardError,
            timedOut
        );
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Best-effort cleanup path.
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup path.
        }
    }
}
