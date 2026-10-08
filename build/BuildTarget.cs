/// <summary>
/// Host operating system a build target can run on.
/// </summary>
enum TargetHost
{
    Any,
    Windows,
    Linux
}

/// <summary>
/// Named build step. Dependencies always run first; the step itself is skipped when the
/// current host does not match <see cref="Host"/>. A target without <see cref="Execute"/>
/// only groups its dependencies.
/// </summary>
sealed record BuildTarget(
    string Name,
    string Description,
    TargetHost Host,
    IReadOnlyList<string> DependsOn,
    Func<Task>? Execute)
{
    /// <summary>Whether the step can run on the current operating system.</summary>
    public bool SupportsCurrentHost => Host switch
    {
        TargetHost.Windows => OperatingSystem.IsWindows(),
        TargetHost.Linux => OperatingSystem.IsLinux(),
        _ => true
    };
}
