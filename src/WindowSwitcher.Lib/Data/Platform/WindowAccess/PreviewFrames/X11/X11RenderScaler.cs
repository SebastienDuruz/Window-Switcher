using Serilog;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;

/// <summary>
/// Downscales a window pixmap on the X server with a chain of bilinear XRender composites,
/// halving the size at each stage so the readback only transfers a thumbnail-sized image.
/// </summary>
/// <remarks>
/// Not thread-safe: the owning capture session calls it under its own lock, on its display.
/// </remarks>
internal sealed class X11RenderScaler : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<X11RenderScaler>();

    private readonly IntPtr _display;
    private readonly IntPtr _sourcePicture;
    private readonly Stage[] _stages;
    private bool _disposed;

    private X11RenderScaler(IntPtr display, IntPtr sourcePicture, Stage[] stages)
    {
        _display = display;
        _sourcePicture = sourcePicture;
        _stages = stages;
    }

    /// <summary>
    /// Width of the final pixmap returned by <see cref="Render"/>.
    /// </summary>
    public int Width => _stages[^1].Width;

    /// <summary>
    /// Height of the final pixmap returned by <see cref="Render"/>.
    /// </summary>
    public int Height => _stages[^1].Height;

    /// <summary>
    /// Builds the downscale chain for a source pixmap.
    /// </summary>
    /// <returns>
    /// The scaler, or <see langword="null"/> when no XRender format matches the visual or no
    /// scaling is needed.
    /// </returns>
    /// <exception cref="Exception">Native calls failed (missing library or entry point).</exception>
    public static X11RenderScaler? TryCreate(
        IntPtr display,
        IntPtr sourcePixmap,
        IntPtr visual,
        int depth,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight
    )
    {
        IReadOnlyList<(int Width, int Height)> sizes = X11RenderScalePlan.ComputeStages(
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight
        );
        if (sizes.Count == 0)
            return null;

        IntPtr format = X11Native.XRenderFindVisualFormat(display, visual);
        if (format == IntPtr.Zero)
            return null;

        IntPtr sourcePicture = IntPtr.Zero;
        var stages = new List<Stage>(sizes.Count);
        try
        {
            sourcePicture = X11Native.XRenderCreatePicture(
                display,
                sourcePixmap,
                format,
                valueMask: 0,
                IntPtr.Zero
            );

            IntPtr previousPicture = sourcePicture;
            int previousWidth = sourceWidth;
            int previousHeight = sourceHeight;
            foreach ((int width, int height) in sizes)
            {
                IntPtr pixmap = X11Native.XCreatePixmap(
                    display,
                    sourcePixmap,
                    (uint)width,
                    (uint)height,
                    (uint)depth
                );
                IntPtr picture = X11Native.XRenderCreatePicture(
                    display,
                    pixmap,
                    format,
                    valueMask: 0,
                    IntPtr.Zero
                );
                stages.Add(new Stage(pixmap, picture, width, height));

                // Each picture is the source of exactly one stage: its transform and filter
                // are set once here and stay valid for every later Render call.
                XTransform transform = X11RenderScalePlan.CreateScaleTransform(
                    previousWidth,
                    previousHeight,
                    width,
                    height
                );
                X11Native.XRenderSetPictureTransform(display, previousPicture, ref transform);
                X11Native.XRenderSetPictureFilter(
                    display,
                    previousPicture,
                    X11Native.RenderFilterBilinear,
                    IntPtr.Zero,
                    0
                );

                previousPicture = picture;
                previousWidth = width;
                previousHeight = height;
            }

            return new X11RenderScaler(display, sourcePicture, [.. stages]);
        }
        catch
        {
            Release(display, sourcePicture, stages);
            throw;
        }
    }

    /// <summary>
    /// Runs the composite chain and returns the final, downscaled pixmap.
    /// </summary>
    public IntPtr Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IntPtr source = _sourcePicture;
        foreach (Stage stage in _stages)
        {
            X11Native.XRenderComposite(
                _display,
                X11Native.PictOpSrc,
                source,
                IntPtr.Zero,
                stage.Picture,
                0,
                0,
                0,
                0,
                0,
                0,
                (uint)stage.Width,
                (uint)stage.Height
            );
            source = stage.Picture;
        }

        return _stages[^1].Pixmap;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release(_display, _sourcePicture, _stages);
    }

    private static void Release(IntPtr display, IntPtr sourcePicture, IReadOnlyList<Stage> stages)
    {
        // Pictures reference their drawables: free every picture before any pixmap.
        FreePicture(display, sourcePicture);
        foreach (Stage stage in stages)
            FreePicture(display, stage.Picture);
        foreach (Stage stage in stages)
            FreePixmap(display, stage.Pixmap);
    }

    private static void FreePicture(IntPtr display, IntPtr picture)
    {
        if (picture == IntPtr.Zero)
            return;

        try
        {
            X11Native.XRenderFreePicture(display, picture);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "XRenderFreePicture failed");
        }
    }

    private static void FreePixmap(IntPtr display, IntPtr pixmap)
    {
        if (pixmap == IntPtr.Zero)
            return;

        try
        {
            _ = X11Native.XFreePixmap(display, pixmap);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "XFreePixmap failed for XRender stage pixmap");
        }
    }

    private readonly record struct Stage(IntPtr Pixmap, IntPtr Picture, int Width, int Height);
}
