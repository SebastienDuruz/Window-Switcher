using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.ViewModels.Abstractions;
using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class GlobalKeyboardStartupStatusServiceTests
{
    [Fact]
    public void ReportStartupFailure_UsesLinuxInputGuidance_WhenAccessIsDenied()
    {
        var sut = new GlobalKeyboardStartupStatusService();

        sut.ReportStartupFailure(new LinuxInputAccessException(["/dev/input/event4"]));

        GlobalKeyboardStartupStatus status = sut.Current;
        Assert.Equal(GlobalKeyboardStartupIssueKind.LinuxInputAccessDenied, status.IssueKind);
        Assert.Contains("/dev/input/event*", status.Message, StringComparison.Ordinal);
        Assert.Equal("sudo usermod -aG input \"$USER\"", status.RemediationCommand);
        Assert.Contains("Sign out", status.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportStartupFailure_UsesGenericGuidance_ForUnexpectedExceptions()
    {
        var sut = new GlobalKeyboardStartupStatusService();

        sut.ReportStartupFailure(new InvalidOperationException("boom"));

        GlobalKeyboardStartupStatus status = sut.Current;
        Assert.Equal(GlobalKeyboardStartupIssueKind.StartupFailure, status.IssueKind);
        Assert.Contains("boom", status.Message, StringComparison.Ordinal);
        Assert.Null(status.RemediationCommand);
        Assert.Contains("restart Window Switcher", status.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportStarted_ClearsPreviousFailure()
    {
        var sut = new GlobalKeyboardStartupStatusService();
        sut.ReportStartupFailure(new InvalidOperationException("boom"));

        sut.ReportStarted();

        Assert.Same(GlobalKeyboardStartupStatus.Available, sut.Current);
    }
}
