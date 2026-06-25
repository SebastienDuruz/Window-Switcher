namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

/// <summary>
/// Reads BGRA frames from a PipeWire-backed GStreamer pipeline.
/// </summary>
public interface IPipeWireRawBgraStream : IDisposable
{
    /// <summary>
    /// Gets whether the underlying pipeline is no longer able to produce frames.
    /// </summary>
    bool IsFaulted { get; }

    /// <summary>
    /// Attempts to pull the next mapped BGRA frame.
    /// </summary>
    /// <param name="timeoutMs">Maximum wait time in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token used to stop waiting.</param>
    /// <returns>A mapped frame when one is available; otherwise <see langword="null" />.</returns>
    PipeWireRawBgraFrame? PullFrame(int timeoutMs, CancellationToken cancellationToken);
}
