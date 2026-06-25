namespace WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;

/// <summary>
/// Provides GStreamer command and PipeWire stream helpers.
/// </summary>
public interface IGstLaunchWrapper : ICommandWrapper
{
    /// <summary>
    /// Starts an in-process PipeWire-backed GStreamer pipeline that emits raw BGRA frames.
    /// </summary>
    /// <param name="nodeId">PipeWire node identifier.</param>
    /// <param name="widthPx">Requested frame width in pixels.</param>
    /// <param name="heightPx">Requested frame height in pixels.</param>
    /// <param name="pipeWireRemoteFd">Optional portal-provided PipeWire remote file descriptor.</param>
    /// <returns>A raw BGRA frame stream when the pipeline can be created; otherwise <see langword="null" />.</returns>
    IPipeWireRawBgraStream? StartPipeWireRawBgraStream(
        string nodeId,
        int widthPx,
        int heightPx,
        int? pipeWireRemoteFd = null
    );
}
