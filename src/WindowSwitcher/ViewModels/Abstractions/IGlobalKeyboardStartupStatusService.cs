using System;

namespace WindowSwitcher.ViewModels.Abstractions;

public enum GlobalKeyboardStartupIssueKind
{
    None = 0,
    LinuxInputAccessDenied = 1,
    StartupFailure = 2,
}

public sealed record GlobalKeyboardStartupStatus(
    GlobalKeyboardStartupIssueKind IssueKind,
    string Message,
    string? RemediationCommand,
    string? Guidance
)
{
    public static GlobalKeyboardStartupStatus Available { get; } =
        new(GlobalKeyboardStartupIssueKind.None, string.Empty, null, null);

    public bool HasIssue => IssueKind != GlobalKeyboardStartupIssueKind.None;
}

public interface IGlobalKeyboardStartupStatusService
{
    event EventHandler? StatusChanged;

    GlobalKeyboardStartupStatus Current { get; }

    void ReportStarted();

    void ReportStartupFailure(Exception exception);
}
