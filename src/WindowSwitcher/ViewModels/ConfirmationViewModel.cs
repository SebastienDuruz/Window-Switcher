using System;
using CommunityToolkit.Mvvm.Input;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the confirmation dialog: shows a request and reports whether the user
/// confirmed or cancelled it.
/// </summary>
public sealed class ConfirmationViewModel
{
    public ConfirmationViewModel(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Request = request;
        ConfirmCommand = new RelayCommand(() => Complete(true));
        CancelCommand = new RelayCommand(() => Complete(false));
    }

    /// <summary>
    /// Raised once with <see langword="true"/> when the user confirms, or
    /// <see langword="false"/> when the user cancels.
    /// </summary>
    public event EventHandler<bool>? Completed;

    public ConfirmationRequest Request { get; }
    public IRelayCommand ConfirmCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public bool IsCompleted { get; private set; }

    private void Complete(bool confirmed)
    {
        if (IsCompleted)
            return;

        IsCompleted = true;
        Completed?.Invoke(this, confirmed);
    }
}
