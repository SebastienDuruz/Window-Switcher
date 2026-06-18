using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Tests;

internal sealed class ImmediateViewModelDispatcher : IViewModelDispatcher
{
    public bool CheckAccess()
    {
        return true;
    }

    public void Post(Action action)
    {
        action();
    }

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
