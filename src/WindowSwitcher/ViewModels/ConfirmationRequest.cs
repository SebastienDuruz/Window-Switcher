using System;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Content of a confirmation dialog shown before a destructive action.
/// </summary>
public sealed record ConfirmationRequest
{
    public ConfirmationRequest(string title, string message, string confirmLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmLabel);

        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
}
