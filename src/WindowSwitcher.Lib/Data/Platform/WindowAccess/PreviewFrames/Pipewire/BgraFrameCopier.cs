namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

internal static class BgraFrameCopier
{
    private const int MaximumFrameBytes = 16 * 1024 * 1024;

    internal static bool TryCopyOrScale(
        IntPtr source,
        int sourceLength,
        int sourceStride,
        int sourceWidth,
        int sourceHeight,
        IntPtr destination,
        int destinationWidth,
        int destinationHeight
    ) =>
        TryCopyOrScale(
            source,
            sourceLength,
            sourceStride,
            sourceWidth,
            sourceHeight,
            WindowSwitcherPipeWireNative.PixelFormat.Bgra,
            destination,
            destinationWidth,
            destinationHeight
        );

    internal static unsafe bool TryCopyOrScale(
        IntPtr source,
        int sourceLength,
        int sourceStride,
        int sourceWidth,
        int sourceHeight,
        WindowSwitcherPipeWireNative.PixelFormat pixelFormat,
        IntPtr destination,
        int destinationWidth,
        int destinationHeight
    )
    {
        BgraScalePlan? scalePlan = null;
        return TryCopyOrScale(
            source,
            sourceLength,
            sourceStride,
            sourceWidth,
            sourceHeight,
            pixelFormat,
            destination,
            destinationWidth,
            destinationHeight,
            ref scalePlan
        );
    }

    internal static unsafe bool TryCopyOrScale(
        IntPtr source,
        int sourceLength,
        int sourceStride,
        int sourceWidth,
        int sourceHeight,
        WindowSwitcherPipeWireNative.PixelFormat pixelFormat,
        IntPtr destination,
        int destinationWidth,
        int destinationHeight,
        ref BgraScalePlan? scalePlan
    )
    {
        if (
            source == IntPtr.Zero
            || destination == IntPtr.Zero
            || sourceLength <= 0
            || sourceWidth <= 0
            || sourceHeight <= 0
            || destinationWidth <= 0
            || destinationHeight <= 0
            || !Enum.IsDefined(pixelFormat)
        )
            return false;

        int absoluteStride;
        int sourceRowBytes;
        int requiredSourceBytes;
        int destinationStride;
        try
        {
            absoluteStride = Math.Abs(sourceStride);
            sourceRowBytes = checked(sourceWidth * 4);
            if (absoluteStride < sourceRowBytes)
                return false;
            requiredSourceBytes = checked(
                checked(absoluteStride * (sourceHeight - 1)) + sourceRowBytes
            );
            destinationStride = checked(destinationWidth * 4);
            if (checked(destinationStride * destinationHeight) > MaximumFrameBytes)
                return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        if (requiredSourceBytes > sourceLength)
            return false;

        byte* sourceStart = (byte*)source;
        if (sourceStride < 0)
            sourceStart += absoluteStride * (sourceHeight - 1);
        byte* destinationStart = (byte*)destination;

        if (sourceWidth == destinationWidth && sourceHeight == destinationHeight)
        {
            CopyWithoutScaling(
                sourceStart,
                sourceStride,
                destinationStart,
                destinationStride,
                destinationWidth,
                destinationHeight,
                pixelFormat
            );
            return true;
        }

        if (
            scalePlan is null
            || !scalePlan.Matches(
                sourceWidth,
                sourceHeight,
                destinationWidth,
                destinationHeight
            )
        )
        {
            scalePlan = BgraScalePlan.Create(
                sourceWidth,
                sourceHeight,
                destinationWidth,
                destinationHeight
            );
        }
        ScaleBilinear(
            sourceStart,
            sourceStride,
            destinationStart,
            destinationStride,
            scalePlan.Horizontal,
            scalePlan.Vertical,
            pixelFormat
        );
        return true;
    }

    private static unsafe void CopyWithoutScaling(
        byte* source,
        int sourceStride,
        byte* destination,
        int destinationStride,
        int width,
        int height,
        WindowSwitcherPipeWireNative.PixelFormat format
    )
    {
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++)
        {
            byte* sourceRow = source + y * sourceStride;
            byte* destinationRow = destination + y * destinationStride;
            if (format == WindowSwitcherPipeWireNative.PixelFormat.Bgra)
            {
                Buffer.MemoryCopy(sourceRow, destinationRow, destinationStride, rowBytes);
                continue;
            }

            if (format == WindowSwitcherPipeWireNative.PixelFormat.Bgrx)
            {
                Buffer.MemoryCopy(sourceRow, destinationRow, destinationStride, rowBytes);
                for (int x = 0; x < width; x++)
                    destinationRow[x * 4 + 3] = 255;
                continue;
            }

            bool forceAlpha = format == WindowSwitcherPipeWireNative.PixelFormat.Rgbx;
            for (int x = 0; x < width; x++)
            {
                byte* input = sourceRow + x * 4;
                byte* output = destinationRow + x * 4;
                output[0] = input[2];
                output[1] = input[1];
                output[2] = input[0];
                output[3] = forceAlpha ? (byte)255 : input[3];
            }
        }
    }

    private static unsafe void ScaleBilinear(
        byte* source,
        int sourceStride,
        byte* destination,
        int destinationStride,
        IReadOnlyList<BgraScaleAxisPoint> horizontal,
        IReadOnlyList<BgraScaleAxisPoint> vertical,
        WindowSwitcherPipeWireNative.PixelFormat format
    )
    {
        for (int y = 0; y < vertical.Count; y++)
        {
            BgraScaleAxisPoint verticalPoint = vertical[y];
            byte* firstRow = source + verticalPoint.First * sourceStride;
            byte* secondRow = source + verticalPoint.Second * sourceStride;
            byte* outputRow = destination + y * destinationStride;
            for (int x = 0; x < horizontal.Count; x++)
            {
                BgraScaleAxisPoint horizontalPoint = horizontal[x];
                byte* output = outputRow + x * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int inputChannel = MapInputChannel(format, channel);
                    output[channel] = inputChannel < 0
                        ? (byte)255
                        : Interpolate(
                            firstRow,
                            secondRow,
                            horizontalPoint,
                            verticalPoint.Weight,
                            inputChannel
                        );
                }
            }
        }
    }

    private static unsafe byte Interpolate(
        byte* firstRow,
        byte* secondRow,
        BgraScaleAxisPoint horizontal,
        double verticalWeight,
        int channel
    )
    {
        double top =
            firstRow[horizontal.First * 4 + channel] * (1 - horizontal.Weight)
            + firstRow[horizontal.Second * 4 + channel] * horizontal.Weight;
        double bottom =
            secondRow[horizontal.First * 4 + channel] * (1 - horizontal.Weight)
            + secondRow[horizontal.Second * 4 + channel] * horizontal.Weight;
        return (byte)Math.Clamp(
            (int)Math.Round(top * (1 - verticalWeight) + bottom * verticalWeight),
            0,
            255
        );
    }

    private static int MapInputChannel(
        WindowSwitcherPipeWireNative.PixelFormat format,
        int outputChannel
    )
    {
        if (
            outputChannel == 3
            && format
                is WindowSwitcherPipeWireNative.PixelFormat.Bgrx
                    or WindowSwitcherPipeWireNative.PixelFormat.Rgbx
        )
            return -1;
        if (
            format
                is WindowSwitcherPipeWireNative.PixelFormat.Bgra
                    or WindowSwitcherPipeWireNative.PixelFormat.Bgrx
        )
            return outputChannel;
        return outputChannel switch
        {
            0 => 2,
            1 => 1,
            2 => 0,
            3 => 3,
            _ => -1,
        };
    }
}

internal sealed class BgraScalePlan
{
    private BgraScalePlan(
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight
    )
    {
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        DestinationWidth = destinationWidth;
        DestinationHeight = destinationHeight;
        Horizontal = CreateAxisMap(sourceWidth, destinationWidth);
        Vertical = CreateAxisMap(sourceHeight, destinationHeight);
    }

    private int SourceWidth { get; }
    private int SourceHeight { get; }
    private int DestinationWidth { get; }
    private int DestinationHeight { get; }
    internal IReadOnlyList<BgraScaleAxisPoint> Horizontal { get; }
    internal IReadOnlyList<BgraScaleAxisPoint> Vertical { get; }

    internal static BgraScalePlan Create(
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight
    ) => new(sourceWidth, sourceHeight, destinationWidth, destinationHeight);

    internal bool Matches(
        int sourceWidth,
        int sourceHeight,
        int destinationWidth,
        int destinationHeight
    ) =>
        SourceWidth == sourceWidth
        && SourceHeight == sourceHeight
        && DestinationWidth == destinationWidth
        && DestinationHeight == destinationHeight;

    private static BgraScaleAxisPoint[] CreateAxisMap(int sourceSize, int destinationSize)
    {
        var result = new BgraScaleAxisPoint[destinationSize];
        for (int destination = 0; destination < destinationSize; destination++)
        {
            double source =
                destinationSize == 1
                    ? 0
                    : (double)destination * (sourceSize - 1) / (destinationSize - 1);
            int first = (int)source;
            result[destination] = new BgraScaleAxisPoint(
                first,
                Math.Min(first + 1, sourceSize - 1),
                source - first
            );
        }
        return result;
    }
}

internal readonly record struct BgraScaleAxisPoint(int First, int Second, double Weight);
