using System;
using System.Threading.Tasks;

namespace WindowSwitcher.Windows.Services;

internal sealed class RenameDialogService
{
    private TaskCompletionSource<string?>? _pendingRenameCompletion;

    public Task<string?> StartSession()
    {
        _pendingRenameCompletion?.TrySetResult(null);
        _pendingRenameCompletion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        return _pendingRenameCompletion.Task;
    }

    public void Cancel()
    {
        Complete(null);
    }

    public void Confirm(string? newWindowTitle)
    {
        string normalizedTitle = (newWindowTitle ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            Complete(null);
            return;
        }

        Complete(normalizedTitle);
    }

    private void Complete(string? newWindowTitle)
    {
        _pendingRenameCompletion?.TrySetResult(newWindowTitle);
        _pendingRenameCompletion = null;
    }
}
