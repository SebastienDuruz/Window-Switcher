using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.ViewModels.Abstractions;

public interface IWindowSnapshotProvider
{
    Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
        CancellationToken cancellationToken = default
    );
}
