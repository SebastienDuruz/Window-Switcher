using System.Windows.Input;

namespace WindowSwitcherLib.WindowAccess.CustomWindows.Commands;

public class ContextMenuCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public ContextMenuCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;
}