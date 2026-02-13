using System.Diagnostics;

namespace WindowSwitcherLib.Infrastructure.Platform.Commands;

/// <summary>
/// Linux adapter for <see cref="WindowSwitcherLib.Application.Platform.ICommandRunner"/>.
/// </summary>
public sealed class LinuxCommandRunner : ProcessCommandRunnerBase
{
    protected override void ConfigureShellCommand(ProcessStartInfo startInfo, string shellCommand)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellCommand);

        startInfo.FileName = "/bin/bash";
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(shellCommand);
    }
}
