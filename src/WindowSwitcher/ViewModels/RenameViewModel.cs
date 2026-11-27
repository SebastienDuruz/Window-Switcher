using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the rename dialog: edits a window title and completes a session when the
/// user confirms or cancels, returning the normalized confirmed title when any.
/// </summary>
public partial class RenameViewModel : ObservableObject
{
    private TaskCompletionSource<string?>? _session;

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _result = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOriginalTitle))]
    [NotifyCanExecuteChangedFor(nameof(RestoreOriginalCommand))]
    private string _originalTitle = string.Empty;

    /// <summary>
    /// Gets whether the window was renamed earlier and can get its original title back.
    /// </summary>
    public bool HasOriginalTitle => !string.IsNullOrWhiteSpace(OriginalTitle);

    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Completes the session with the original title.
    /// </summary>
    public IRelayCommand RestoreOriginalCommand { get; }

    public RenameViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
        RestoreOriginalCommand = new RelayCommand(
            () => Complete(OriginalTitle.Trim()),
            () => HasOriginalTitle
        );
    }

    /// <param name="initialTitle">Current title, shown in the text box.</param>
    /// <param name="originalTitle">Title before the first rename, or <see langword="null"/>
    /// when the window was never renamed.</param>
    public Task<string?> StartSessionAsync(string initialTitle, string? originalTitle = null)
    {
        _session?.TrySetResult(null);
        _session = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        WindowTitle = initialTitle;
        OriginalTitle = originalTitle ?? string.Empty;
        Result = string.Empty;
        return _session.Task;
    }

    public void CancelSession()
    {
        Complete(null);
    }

    private void Confirm()
    {
        string normalizedTitle = WindowTitle.Trim();
        Complete(string.IsNullOrWhiteSpace(normalizedTitle) ? null : normalizedTitle);
    }

    private void Cancel()
    {
        Complete(null);
    }

    private void Complete(string? result)
    {
        TaskCompletionSource<string?>? session = _session;
        if (session is null)
            return;

        _session = null;
        Result = result ?? string.Empty;
        session.TrySetResult(result);
    }
}
