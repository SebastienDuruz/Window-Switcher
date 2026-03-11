using System.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Commands;

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
        return await ProcessExecution.RunAsync(process, request.Timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    protected abstract void ConfigureShellCommand(ProcessStartInfo startInfo, string shellCommand);
}
