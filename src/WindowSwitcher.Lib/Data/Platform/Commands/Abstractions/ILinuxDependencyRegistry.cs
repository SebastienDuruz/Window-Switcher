namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

/// <summary>
/// Reports missing Linux dependencies used by runtime integrations.
/// </summary>
public interface ILinuxDependencyRegistry
{
    /// <summary>
    /// Records a missing dependency so it can be surfaced once to the user.
    /// </summary>
    void ReportMissingOnce(string dependency);
}
