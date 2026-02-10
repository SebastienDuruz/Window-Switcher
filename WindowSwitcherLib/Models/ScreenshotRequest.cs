namespace WindowSwitcherLib.Domain.Models;

public readonly record struct ScreenshotRequest(
    int? MaxWidthPx = null,
    int? MaxHeightPx = null,
    int TimeoutMs = 1500
);

