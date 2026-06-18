using System;
using System.Threading.Tasks;

namespace WindowSwitcher.ViewModels.Abstractions;

public interface IViewModelDispatcher
{
    bool CheckAccess();

    void Post(Action action);

    Task InvokeAsync(Action action);
}
