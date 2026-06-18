using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class AvaloniaViewModelDispatcher : IViewModelDispatcher
{
    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
    }
}
