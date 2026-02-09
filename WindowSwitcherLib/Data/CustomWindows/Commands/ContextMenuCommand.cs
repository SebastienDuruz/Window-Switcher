using System.Windows.Input;

namespace WindowSwitcherLib.Data.CustomWindows.Commands;

public class ContextMenuCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public event EventHandler? CanExecuteChanged;
}