namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

/// <summary>
/// Provides Linux dependency checks used to select runtime behaviors.
/// </summary>
public interface ILinuxDependencyRegistry
{
    /// <summary>
    /// Gets whether <c>wmctrl</c> is available.
    /// </summary>
    bool IsWmctrlAvailable { get; }

    /// <summary>
    /// Records a missing dependency so it can be surfaced once to the user.
    /// </summary>
    void ReportMissingOnce(string dependency);
}
