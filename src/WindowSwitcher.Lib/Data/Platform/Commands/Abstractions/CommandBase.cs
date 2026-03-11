using System.Diagnostics;

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

    private static string ExecuteProcess(Process process, int timeoutMs)
    {
        try
        {
            var result = ProcessExecution
                .RunAsync(process, TimeSpan.FromMilliseconds(timeoutMs))
                .GetAwaiter()
                .GetResult();
            return result.IsSuccess ? result.StandardOutput : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
