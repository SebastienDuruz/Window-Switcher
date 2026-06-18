using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class WindowAccessorWindowSnapshotProvider : IWindowSnapshotProvider
{
    private readonly WinAccessorBase _winAccessorBase;

    public WindowAccessorWindowSnapshotProvider(WinAccessorBase winAccessorBase)
    {
        ArgumentNullException.ThrowIfNull(winAccessorBase);

        _winAccessorBase = winAccessorBase;
    }

    public Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        return _winAccessorBase.GetWindowsAsync(cancellationToken);
    }
}
