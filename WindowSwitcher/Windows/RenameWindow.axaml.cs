using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowSwitcher.Windows.Services;

namespace WindowSwitcher.Windows;

public partial class RenameWindow : Window
{
    private readonly UtilityWindowService _windowService = new();
    private readonly RenameDialogService _renameDialogService = new();

    public bool IsUpdated { get; set; } = false;
    public string NewWindowTitle { get; set; } = string.Empty;

    public RenameWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public async Task<bool> ShowAndWaitForResultAsync(string initialTitle)
    {
        Task<string?> renameSessionTask = _renameDialogService.StartSession();
        IsUpdated = false;
        NewWindowTitle = string.Empty;

        WindowTitleTextBox.Text = initialTitle;

        if (IsVisible)
            Activate();
        else
            Show();

        WindowTitleTextBox.Focus();
        WindowTitleTextBox.CaretIndex = WindowTitleTextBox.Text?.Length ?? 0;

        string? renamedTitle = await renameSessionTask;
        IsUpdated = !string.IsNullOrWhiteSpace(renamedTitle);
        NewWindowTitle = renamedTitle ?? string.Empty;
        return IsUpdated;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = _windowService.HandleClosing(this, e);
        _renameDialogService.Cancel();
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        _renameDialogService.Cancel();
        Hide();
    }

    private void RenameButtonClick(object? sender, RoutedEventArgs e)
    {
        _renameDialogService.Confirm(WindowTitleTextBox.Text);
        Hide();
    }
}
