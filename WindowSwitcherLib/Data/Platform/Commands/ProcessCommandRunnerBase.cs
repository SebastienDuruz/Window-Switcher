using System.Diagnostics;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.Data.Platform.Commands;

/// <summary>
/// Shared process execution infrastructure for command runner adapters.
/// </summary>
public abstract class ProcessCommandRunnerBase : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        CommandRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Timeout must be greater than zero."
            );

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(request.ShellCommand))
        {
            ConfigureShellCommand(startInfo, request.ShellCommand);
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutablePath);
            startInfo.FileName = request.ExecutablePath;
            foreach (string argument in request.Arguments)
                startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            startInfo.WorkingDirectory = request.WorkingDirectory;

        foreach ((string key, string value) in request.EnvironmentVariables)
            startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
            throw new InvalidOperationException(
                $"Failed to start process for request '{request.ExecutablePath ?? request.ShellCommand}'."
            );

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            string standardOutput = await stdOutTask.ConfigureAwait(false);
            string standardError = await stdErrTask.ConfigureAwait(false);
            return new CommandResult(
                process.ExitCode,
                standardOutput,
                standardError,
                TimedOut: false
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            string standardOutput = await stdOutTask.ConfigureAwait(false);
            string standardError = await stdErrTask.ConfigureAwait(false);
            return new CommandResult(-1, standardOutput, standardError, TimedOut: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw;
        }
    }

    protected abstract void ConfigureShellCommand(ProcessStartInfo startInfo, string shellCommand);

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore cleanup exceptions.
        }
    }
}
