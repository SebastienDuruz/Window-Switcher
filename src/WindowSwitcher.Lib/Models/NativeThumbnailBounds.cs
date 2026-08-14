namespace WindowSwitcher.Lib.Models;

/// <summary>
/// Destination rectangle used when rendering a native window thumbnail.
/// </summary>
/// <param name="Left">Left coordinate in physical pixels.</param>
/// <param name="Top">Top coordinate in physical pixels.</param>
/// <param name="Right">Right coordinate in physical pixels.</param>
/// <param name="Bottom">Bottom coordinate in physical pixels.</param>
public readonly record struct NativeThumbnailBounds(int Left, int Top, int Right, int Bottom);
