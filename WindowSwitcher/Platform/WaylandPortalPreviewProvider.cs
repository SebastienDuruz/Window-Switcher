using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WindowSwitcherLib.Data.WindowAccess;

namespace WindowSwitcher.Platform;

public sealed class WaylandPortalPreviewProvider : IDisposable
{
    private static readonly Lazy<WaylandPortalPreviewProvider> Instance = new(() => new WaylandPortalPreviewProvider());
    public static WaylandPortalPreviewProvider GetInstance() => Instance.Value;

    private readonly object _syncRoot = new();
    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;

    private DateTimeOffset _lastStartAttemptUtc = DateTimeOffset.MinValue;
    private Task? _startTask;

    private WaylandPortalPreviewProvider()
    {
    }

    public Task EnsureStarted(CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Task.CompletedTask;

        lock (_syncRoot)
        {
            if (WaylandPortalScreencastCapture.GetInstance().IsRunning)
                return Task.CompletedTask;

            if (_startTask is { IsCompleted: false })
                return _startTask;

            // Prevent tight retry loops if the portal is denied or unavailable.
            if (DateTimeOffset.UtcNow - _lastStartAttemptUtc < TimeSpan.FromSeconds(10))
                return Task.CompletedTask;

            _lastStartAttemptUtc = DateTimeOffset.UtcNow;
            _startTask = Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = WaylandPortalScreencastCapture.GetInstance().EnsureStarted();
            }, cancellationToken);

            return _startTask;
        }
    }

    public bool TryGetLatestBitmap(out Bitmap? bitmap)
    {
        bitmap = null;

        WaylandPortalScreencastCapture capture = WaylandPortalScreencastCapture.GetInstance();
        if (!capture.TryGetLatestFrame(out byte[]? frame, out int width, out int height))
            return false;

        if (frame is null || width <= 0 || height <= 0)
            return false;

        int bytesPerRow = checked(width * 4);
        int requiredBytes = checked(bytesPerRow * height);
        if (frame.Length < requiredBytes)
            return false;

        lock (_syncRoot)
        {
            EnsureBitmapLocked(width, height);
            ArgumentNullException.ThrowIfNull(_bitmap);

            using var fb = _bitmap.Lock();
            for (int y = 0; y < height; y++)
            {
                IntPtr destRow = IntPtr.Add(fb.Address, y * fb.RowBytes);
                Marshal.Copy(frame, y * bytesPerRow, destRow, bytesPerRow);
            }

            bitmap = _bitmap;
            return true;
        }
    }

    private void EnsureBitmapLocked(int width, int height)
    {
        if (_bitmap is not null && _bitmapWidth == width && _bitmapHeight == height)
            return;

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        _bitmapWidth = width;
        _bitmapHeight = height;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmapWidth = 0;
            _bitmapHeight = 0;
        }
    }
}
