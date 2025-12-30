using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public abstract class CommandBase
{
    protected Process _process;

    public CommandBase(string command)
    {
        _process = new Process();
        _process.StartInfo.FileName = command;
        _process.StartInfo.UseShellExecute = false;
        _process.StartInfo.RedirectStandardOutput = true;
        _process.StartInfo.CreateNoWindow = true;
    }
}