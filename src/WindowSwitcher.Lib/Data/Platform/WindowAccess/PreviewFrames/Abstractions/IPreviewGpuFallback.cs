namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

/// <summary>
/// Allows a preview consumer to reject GPU-backed frames after renderer initialization fails.
/// </summary>
public interface IPreviewGpuFallback
{
    /// <summary>
    /// Disables GPU-backed frames for the specified window and requests a CPU-buffer renegotiation.
    /// </summary>
    /// <param name="windowId">Platform window identifier.</param>
    void DisableGpuFrames(string windowId);
}
