using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public class WmctrlWrapper() : CommandBase("wmctrl"), ICommandWrapper
{
    public string Execute(string args)
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return ExecuteWithArguments(args, timeoutMs: 2_000);
    }

    public async Task<string> ExecuteAsync(
        string args,
        CancellationToken cancellationToken = default
    )
    {
        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return await ExecuteWithArgumentsAsync(args, timeoutMs: 2_000, cancellationToken)
            .ConfigureAwait(false);
    }

    public string Execute(IReadOnlyList<string> args, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return ExecuteWithArgumentList(args, timeoutMs);
    }

    public async Task<string> ExecuteAsync(
        IReadOnlyList<string> args,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!LinuxDependencies.IsWmctrlAvailable)
        {
            LinuxDependencies.ReportMissingOnce("wmctrl");
            return string.Empty;
        }

        return await ExecuteWithArgumentListAsync(args, timeoutMs, cancellationToken)
            .ConfigureAwait(false);
    }
}
