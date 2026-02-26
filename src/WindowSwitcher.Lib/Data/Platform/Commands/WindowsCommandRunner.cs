using System.Diagnostics;
using WindowSwitcherLib.Data.Platform.SystemInfo.Abstractions;

namespace WindowSwitcherLib.Data.Platform.Commands;

/// <summary>
/// Windows adapter for <see cref="ICommandRunner"/>.
/// </summary>
public sealed class WindowsCommandRunner : ProcessCommandRunnerBase
{
    protected override void ConfigureShellCommand(ProcessStartInfo startInfo, string shellCommand)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellCommand);

        startInfo.FileName = "cmd.exe";
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(shellCommand);
    }
}
