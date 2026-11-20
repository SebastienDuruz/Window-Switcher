using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.X11;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class X11IntegrationCollection
{
    // The capture paths are selected through process-wide environment variables.
    public const string Name = "X11 integration";
}

/// <summary>
/// Captures a real window through Composite, Damage, MIT-SHM and RENDER. These paths depend on
/// the X server ABI (structure layouts, pixel formats) that unit tests cannot reproduce.
/// Run in CI under Xvfb; skipped when no X server is available.
/// </summary>
[Collection(X11IntegrationCollection.Name)]
[Trait("Category", "Integration")]
[SupportedOSPlatform("linux")]
public sealed class X11CaptureIntegrationTests
{
    private const string DisableShm = "WINDOW_SWITCHER_X11_DISABLE_SHM";
    private const string DisableRender = "WINDOW_SWITCHER_X11_DISABLE_RENDER";

    public static TheoryData<bool, bool> CapturePaths =>
        new()
        {
            { false, false },
            { true, false },
            { false, true },
            { true, true },
        };

    [Theory]
    [MemberData(nameof(CapturePaths))]
    public void FullSizeCapture_ReturnsTheWindowPixels(bool disableShm, bool disableRender)
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using X11WindowCaptureSession session = CreateSession(window, disableShm, disableRender);

        using NativeBgraPreviewFrame frame = CaptureMatching(
            session,
            new ScreenshotRequest(),
            64,
            48
        );

        AssertColor(X11TestWindow.Red, frame, 4, 24);
        AssertColor(X11TestWindow.Blue, frame, 59, 24);
    }

    [Theory]
    [MemberData(nameof(CapturePaths))]
    public void DownscaledCapture_KeepsOrientationAndColours(bool disableShm, bool disableRender)
    {
        using X11TestWindow window = X11TestWindow.Create(256, 128);
        using X11WindowCaptureSession session = CreateSession(window, disableShm, disableRender);

        using NativeBgraPreviewFrame frame = CaptureMatching(
            session,
            new ScreenshotRequest(MaxWidthPx: 64, MaxHeightPx: 64),
            64,
            32
        );

        AssertColor(X11TestWindow.Red, frame, 8, 16);
        AssertColor(X11TestWindow.Blue, frame, 55, 16);
    }

    [Fact]
    public void Capture_FollowsWindowResize()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using X11WindowCaptureSession session = CreateSession(window, false, false);
        CaptureMatching(session, new ScreenshotRequest(), 64, 48).Dispose();

        window.Resize(96, 40);

        using NativeBgraPreviewFrame frame = CaptureMatching(
            session,
            new ScreenshotRequest(),
            96,
            40
        );
        AssertColor(X11TestWindow.Blue, frame, 90, 20);
    }

    [Fact]
    public async Task WaitForDamage_ReportsDrawingAndTimesOutWhenIdle()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using X11WindowCaptureSession session = CreateSession(window, false, false);
        // Consume the damage produced while the window was created.
        while (await session.WaitForDamageAsync(100, CancellationToken.None)) { }

        bool idle = await session.WaitForDamageAsync(100, CancellationToken.None);
        window.Paint(X11TestWindow.Blue, X11TestWindow.Red);
        bool afterDrawing = await session.WaitForDamageAsync(2000, CancellationToken.None);

        Assert.False(idle);
        Assert.True(afterDrawing);
        using NativeBgraPreviewFrame frame = CaptureMatching(
            session,
            new ScreenshotRequest(),
            64,
            48,
            pixel => (pixel & 0xFFFFFF) == X11TestWindow.Blue
        );
        AssertColor(X11TestWindow.Blue, frame, 4, 24);
    }

    [Fact]
    public async Task Capture_FollowsTheWindowAfterUnmapAndRemap()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using X11WindowCaptureSession session = CreateSession(window, false, false);
        CaptureMatching(session, new ScreenshotRequest(), 64, 48).Dispose();

        // Minimizing and restoring unmaps and maps the window, which reallocates its pixmap.
        window.Remap();
        window.Paint(X11TestWindow.Blue, X11TestWindow.Red);
        Assert.True(await session.WaitForDamageAsync(2000, CancellationToken.None));

        using NativeBgraPreviewFrame frame = CaptureMatching(
            session,
            new ScreenshotRequest(),
            64,
            48,
            pixel => (pixel & 0xFFFFFF) == X11TestWindow.Blue
        );
        AssertColor(X11TestWindow.Red, frame, 59, 24);
    }

    [Fact]
    public void Capture_FailsCleanly_AfterTheWindowIsDestroyed()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using X11WindowCaptureSession session = CreateSession(window, false, false);
        CaptureMatching(session, new ScreenshotRequest(), 64, 48).Dispose();

        window.Destroy();

        Assert.False(
            session.TryCaptureFrame(new ScreenshotRequest(), out NativeBgraPreviewFrame? frame)
        );
        Assert.Null(frame);
    }

    [Fact]
    public void Capture_ReturnsNothing_AfterTheSessionIsDisposed()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        X11WindowCaptureSession session = CreateSession(window, false, false);

        session.Dispose();
        session.Dispose();

        Assert.False(session.TryCaptureFrame(new ScreenshotRequest(), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x0")]
    [InlineData("0xZZ")]
    [InlineData("-5")]
    public void TryCreate_RejectsInvalidWindowIds(string windowId)
    {
        Assert.Null(X11WindowCaptureSession.TryCreate(windowId));
    }

    [Fact]
    public void EwmhClient_RenamesTheWindowInBothTitleProperties()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        using var client = new X11EwmhClient();

        bool renamed = client.TryRenameWindow(window.Id, "Fenêtre de test");

        Assert.True(renamed);
        Assert.Equal("Fenêtre de test", window.ReadUtf8Property("_NET_WM_NAME"));
        Assert.Equal(
            "Fenêtre de test"u8.Length - 1,
            window.ReadProperty("WM_NAME", "STRING")?.Length
        );
    }

    [Fact]
    public void EwmhClient_RefusesToRenameAMissingWindow()
    {
        using X11TestWindow window = X11TestWindow.Create(64, 48);
        uint destroyedId = window.Id;
        window.Destroy();
        using var client = new X11EwmhClient();

        Assert.False(client.TryRenameWindow(destroyedId, "Ghost"));
    }

    private static X11WindowCaptureSession CreateSession(
        X11TestWindow window,
        bool disableShm,
        bool disableRender
    )
    {
        string? previousShm = Environment.GetEnvironmentVariable(DisableShm);
        string? previousRender = Environment.GetEnvironmentVariable(DisableRender);
        Environment.SetEnvironmentVariable(DisableShm, disableShm ? "1" : null);
        Environment.SetEnvironmentVariable(DisableRender, disableRender ? "1" : null);
        try
        {
            X11WindowCaptureSession? session = X11WindowCaptureSession.TryCreate(window.IdText);
            Assert.NotNull(session);
            // Without a compositing manager (Xvfb), the session redirects the window itself and
            // the new backing pixmap starts empty. A real client repaints on Expose; the test
            // window does not listen to events, so it repaints here.
            window.Paint();
            // The first capture reads both variables (SHM image and RENDER probe).
            session.TryCaptureFrame(new ScreenshotRequest(), out NativeBgraPreviewFrame? first);
            first?.Dispose();
            return session;
        }
        finally
        {
            Environment.SetEnvironmentVariable(DisableShm, previousShm);
            Environment.SetEnvironmentVariable(DisableRender, previousRender);
        }
    }

    /// <summary>
    /// Captures until the frame has the expected size and the window content has reached the
    /// composite pixmap; X servers apply drawing asynchronously.
    /// </summary>
    private static NativeBgraPreviewFrame CaptureMatching(
        X11WindowCaptureSession session,
        ScreenshotRequest request,
        int expectedWidth,
        int expectedHeight,
        Func<uint, bool>? leftPixelIsReady = null
    )
    {
        leftPixelIsReady ??= pixel => pixel != 0;
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        NativeBgraPreviewFrame? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last?.Dispose();
            last = null;
            if (
                session.TryCaptureFrame(request, out last)
                && last is not null
                && last.WidthPx == expectedWidth
                && last.HeightPx == expectedHeight
                && leftPixelIsReady(ReadPixel(last, 1, expectedHeight / 2))
            )
                return last;
            Thread.Sleep(20);
        }

        last?.Dispose();
        Assert.Fail($"No {expectedWidth}x{expectedHeight} frame was captured within 3 seconds.");
        return null!;
    }

    private static void AssertColor(uint expectedRgb, NativeBgraPreviewFrame frame, int x, int y)
    {
        Assert.Equal(
            expectedRgb.ToString("X6"),
            (ReadPixel(frame, x, y) & 0xFFFFFF).ToString("X6")
        );
    }

    private static uint ReadPixel(NativeBgraPreviewFrame frame, int x, int y)
    {
        // BGRA in memory is 0xAARRGGBB as a little-endian integer.
        return unchecked((uint)Marshal.ReadInt32(frame.Data, y * frame.Stride + x * 4));
    }
}
