namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

internal static class PreviewFrameTiming
{
    internal const int FramesPerSecond = 20;
    internal static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(
        1000d / FramesPerSecond
    );
}
