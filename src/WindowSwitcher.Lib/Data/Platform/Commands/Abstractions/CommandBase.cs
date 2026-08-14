using System.Diagnostics;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public abstract class CommandBase(string command)
{
    protected string Command { get; } = command;

    protected Process CreateProcess()
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
    }

    protected string ExecuteWithArguments(string arguments, int timeoutMs)
    {
        using Process process = CreateProcess();
        process.StartInfo.Arguments = arguments;
        return ExecuteProcess(process, timeoutMs);
    }

    protected string ExecuteWithArgumentList(IReadOnlyList<string> arguments, int timeoutMs)
    {
        using Process process = CreateProcess();
        process.StartInfo.Arguments = string.Empty;
        process.StartInfo.ArgumentList.Clear();
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        return ExecuteProcess(process, timeoutMs);
    }

    protected async Task<string> ExecuteWithArgumentsAsync(
        string arguments,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        using Process process = CreateProcess();
        process.StartInfo.Arguments = arguments;
        return await ExecuteProcessAsync(process, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
    }

    protected async Task<string> ExecuteWithArgumentListAsync(
        IReadOnlyList<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        using Process process = CreateProcess();
        process.StartInfo.Arguments = string.Empty;
        process.StartInfo.ArgumentList.Clear();
        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        return await ExecuteProcessAsync(process, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ExecuteProcess(Process process, int timeoutMs)
    {
        try
        {
            if (!process.Start())
                return string.Empty;

            string standardOutput = process.StandardOutput.ReadToEnd();
            string _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                TryKillProcess(process);
                return string.Empty;
            }

            return process.ExitCode == 0 ? standardOutput : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> ExecuteProcessAsync(
        Process process,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        try
        {
            CommandResult result = await ProcessExecution
                .RunAsync(process, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken)
                .ConfigureAwait(false);
            return result.IsSuccess ? result.StandardOutput : string.Empty;
        }
        catch
        {
            return string.Empty;
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
