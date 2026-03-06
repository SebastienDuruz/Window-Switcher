namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

public interface ILinuxDependencyRegistry
{
    event Action<string>? DependencyMissing;

    bool IsWmctrlAvailable { get; }
    bool IsImportAvailable { get; }
    bool IsGstLaunchAvailable { get; }
    bool IsPwDumpAvailable { get; }
    bool IsGdbusAvailable { get; }
    bool IsGstPipeWireSrcAvailable { get; }

    IReadOnlyCollection<string> GetReportedMissing();
    void ReportMissingOnce(string dependency);
}
