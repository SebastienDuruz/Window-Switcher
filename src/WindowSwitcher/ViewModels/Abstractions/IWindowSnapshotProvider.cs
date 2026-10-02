using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.ViewModels.Abstractions;

public interface IWindowSnapshotProvider
{
    /// <summary>
    /// Reads the current windows, or returns <see langword="null" /> when they cannot be read.
    /// </summary>
    Task<IReadOnlyCollection<WindowConfig>?> TryGetWindowsAsync(
        CancellationToken cancellationToken = default
    );
}
