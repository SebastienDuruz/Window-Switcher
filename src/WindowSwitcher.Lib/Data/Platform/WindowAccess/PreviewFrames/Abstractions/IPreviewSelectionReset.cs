namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Exposes user-controlled reset operations for preview providers whose source is selected
/// through an interactive system picker.
/// </summary>
public interface IPreviewSelectionReset
{
    /// <summary>
    /// Occurs while the system picker is assigning a source to a preview window.
    /// </summary>
    event EventHandler<PreviewSelectionPromptEventArgs>? SelectionPromptChanged;

    /// <summary>
    /// Clears the remembered selection and restarts capture for one window.
    /// </summary>
    /// <param name="windowId">Identifier of the preview whose source must be selected again.</param>
    void ResetSelection(string windowId);

    /// <summary>
    /// Clears every remembered selection and restarts all active captures.
    /// </summary>
    void ResetAllSelections();
}

/// <summary>
/// Describes whether a system source picker is currently assigning a source to a preview.
/// </summary>
/// <param name="WindowId">Identifier of the preview being assigned.</param>
/// <param name="IsPending"><see langword="true" /> while its picker is active.</param>
public sealed record PreviewSelectionPromptEventArgs(string WindowId, bool IsPending);
