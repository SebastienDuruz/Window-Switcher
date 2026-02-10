using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WindowSwitcherLib.Data;

namespace WindowSwitcher.Windows;

public partial class RenameWindow : Window
{
    private readonly record struct RenameDialogResult(bool IsUpdated, string NewWindowTitle);
    private TaskCompletionSource<RenameDialogResult>? _pendingRenameCompletion;

    public bool IsUpdated { get; set; } = false;
    public string NewWindowTitle { get; set; } = string.Empty;

    public RenameWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public async Task<bool> ShowAndWaitForResultAsync(string initialTitle)
    {
        _pendingRenameCompletion?.TrySetResult(new RenameDialogResult(false, string.Empty));
        _pendingRenameCompletion = new TaskCompletionSource<RenameDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsUpdated = false;
        NewWindowTitle = string.Empty;

        WindowTitleTextBox.Text = initialTitle;

        if (IsVisible)
            Activate();
        else
            Show();

        WindowTitleTextBox.Focus();
        WindowTitleTextBox.CaretIndex = WindowTitleTextBox.Text?.Length ?? 0;

        RenameDialogResult result = await _pendingRenameCompletion.Task;
        IsUpdated = result.IsUpdated;
        NewWindowTitle = result.NewWindowTitle;
        return result.IsUpdated;
    }
    
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !StaticData.AppClosing;
        if (e.Cancel)
        {
            CompleteRename(false, string.Empty);
            Hide();
            return;
        }

        CompleteRename(false, string.Empty);
    }

    private void CancelButtonClick(object? sender, RoutedEventArgs e)
    {
        CompleteRename(false, string.Empty);
        Hide();
    }

    private void RenameButtonClick(object? sender, RoutedEventArgs e)
    {
        string newWindowTitle = WindowTitleTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newWindowTitle))
        {
            CompleteRename(false, string.Empty);
            Hide();
            return;
        }

        CompleteRename(true, newWindowTitle);
        Hide();
    }

    private void CompleteRename(bool isUpdated, string newWindowTitle)
    {
        _pendingRenameCompletion?.TrySetResult(new RenameDialogResult(isUpdated, newWindowTitle));
        _pendingRenameCompletion = null;
    }
}
