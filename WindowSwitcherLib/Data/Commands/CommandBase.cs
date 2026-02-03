using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public abstract class CommandBase
{
    protected string Command { get; }

    protected CommandBase(string command)
    {
        Command = command;
    }

    protected Process CreateProcess()
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
    }
}
