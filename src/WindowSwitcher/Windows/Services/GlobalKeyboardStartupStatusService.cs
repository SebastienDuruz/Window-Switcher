using System;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

public sealed class GlobalKeyboardStartupStatusService : IGlobalKeyboardStartupStatusService
{
    private const string LinuxInputAccessMessage =
        "Global shortcuts are unavailable because Window Switcher cannot read /dev/input/event* on this Linux session.";
    private const string LinuxInputAccessCommand = "sudo usermod -aG input \"$USER\"";
    private const string LinuxInputAccessGuidance =
        "Sign out and sign back in, then restart Window Switcher. If your distro does not use the input group, configure udev rules instead.";
    private const string GenericStartupGuidance =
        "Fix the underlying issue, then restart Window Switcher.";

    private readonly object _syncRoot = new();
    private GlobalKeyboardStartupStatus _current = GlobalKeyboardStartupStatus.Available;

    public event EventHandler? StatusChanged;

    public GlobalKeyboardStartupStatus Current
    {
        get
        {
            lock (_syncRoot)
            {
                return _current;
            }
        }
    }

    public void ReportStarted()
    {
        Update(GlobalKeyboardStartupStatus.Available);
    }

    public void ReportStartupFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is LinuxInputAccessException)
        {
            Update(
                new GlobalKeyboardStartupStatus(
                    GlobalKeyboardStartupIssueKind.LinuxInputAccessDenied,
                    LinuxInputAccessMessage,
                    LinuxInputAccessCommand,
                    LinuxInputAccessGuidance
                )
            );
            return;
        }

        string details = string.IsNullOrWhiteSpace(exception.Message)
            ? "Unexpected startup failure."
            : exception.Message.Trim();
        Update(
            new GlobalKeyboardStartupStatus(
                GlobalKeyboardStartupIssueKind.StartupFailure,
                $"Global shortcuts are unavailable: {details}",
                null,
                GenericStartupGuidance
            )
        );
    }

    private void Update(GlobalKeyboardStartupStatus next)
    {
        bool changed;
        lock (_syncRoot)
        {
            changed = _current != next;
            if (!changed)
                return;

            _current = next;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
