using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

public sealed class GdbusWrapper() : CommandBase("gdbus"), IGdbusWrapper
{
    public string Execute(string args)
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return ExecuteWithArguments(args, timeoutMs: 2_500);
        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }

    public async Task<string> ExecuteAsync(
        string args,
        CancellationToken cancellationToken = default
    )
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return await ExecuteWithArgumentsAsync(args, timeoutMs: 2_500, cancellationToken)
                .ConfigureAwait(false);

        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }

    public string Execute(IReadOnlyList<string> args, int timeoutMs)
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return ExecuteWithArgumentList(args, timeoutMs);
        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }

    public async Task<string> ExecuteAsync(
        IReadOnlyList<string> args,
        int timeoutMs,
        CancellationToken cancellationToken = default
    )
    {
        if (LinuxDependencies.IsGdbusAvailable)
            return await ExecuteWithArgumentListAsync(args, timeoutMs, cancellationToken)
                .ConfigureAwait(false);

        LinuxDependencies.ReportMissingOnce("gdbus");
        return string.Empty;
    }
}
