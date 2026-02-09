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
}
