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
    /// Gets whether <c>import</c> is available.
    /// </summary>
    bool IsImportAvailable { get; }

    /// <summary>
    /// Gets whether <c>gst-launch-1.0</c> is available.
    /// </summary>
    bool IsGstLaunchAvailable { get; }

    /// <summary>
    /// Gets whether <c>pw-dump</c> is available.
    /// </summary>
    bool IsPwDumpAvailable { get; }

    /// <summary>
    /// Gets whether <c>gdbus</c> is available.
    /// </summary>
    bool IsGdbusAvailable { get; }

    /// <summary>
    /// Gets whether the GStreamer <c>pipewiresrc</c> plugin is available.
    /// </summary>
    bool IsGstPipeWireSrcAvailable { get; }

    /// <summary>
    /// Records a missing dependency so it can be surfaced once to the user.
    /// </summary>
    void ReportMissingOnce(string dependency);
}
