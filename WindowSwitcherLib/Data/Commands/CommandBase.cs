using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

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
                CreateNoWindow = true
            }
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
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
