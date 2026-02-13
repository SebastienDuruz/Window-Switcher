using System.Diagnostics;

namespace WindowSwitcherLib.Infrastructure.Platform.Commands;

/// <summary>
/// Windows adapter for <see cref="WindowSwitcherLib.Application.Platform.ICommandRunner"/>.
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
