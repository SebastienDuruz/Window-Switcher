using System.Threading.Tasks;
using Avalonia.Controls;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class RenameWindow : Window
{
    private readonly UtilityWindowService _windowService = new();
    private RenameViewModel ViewModel { get; }

    public string NewWindowTitle => ViewModel.Result;

    public RenameWindow()
    {
        InitializeComponent();
        ViewModel = new RenameViewModel();
        DataContext = ViewModel;
        Closing += OnClosing;
    }

    public async Task<bool> ShowAndWaitForResultAsync(string initialTitle)
    {
        Task<string?> renameSessionTask = ViewModel.StartSessionAsync(initialTitle);

        if (IsVisible)
            Activate();
        else
            Show();

        WindowTitleTextBox.Focus();
        WindowTitleTextBox.CaretIndex = WindowTitleTextBox.Text?.Length ?? 0;

        string? renamedTitle = await renameSessionTask;
        Hide();
        return !string.IsNullOrWhiteSpace(renamedTitle);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.CancelSession();
        _ = _windowService.HandleClosing(this, e);
    }
}
