using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class X11EwmhWindowAccessorTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task NativeAccessor_ReturnsWellFormedWindowIds_WhenDisplayIsAvailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var sut = new X11EwmhWindowAccessor();

        IReadOnlyCollection<WindowConfig> windows = await sut.GetWindowsAsync();

        if (
            string.Equals(
                Environment.GetEnvironmentVariable("WINDOW_SWITCHER_REQUIRE_EWMH_WINDOWS"),
                "1",
                StringComparison.Ordinal
            )
        )
            Assert.NotEmpty(windows);

        Assert.All(
            windows,
            window => Assert.Matches("^0x[0-9a-f]{8}$", window.WindowId)
        );
    }

    [Fact]
    public async Task GetWindowsAsync_MapsEwmhSnapshotAndCachesProcessNames()
    {
        uint processId = unchecked((uint)(Environment.ProcessId + 10_000));
        var client = new FakeX11EwmhClient
        {
            Windows =
            [
                new X11EwmhWindow(0x123, processId, "Terminal"),
                new X11EwmhWindow(0x456, processId, "Editor"),
                new X11EwmhWindow(0x789, (uint)Environment.ProcessId, "Window Switcher"),
            ],
        };
        int resolverCalls = 0;
        using var sut = new X11EwmhWindowAccessor(
            client,
            (pid, _) =>
            {
                resolverCalls++;
                Assert.Equal((int)processId, pid);
                return Task.FromResult("sample-process");
            }
        );

        IReadOnlyCollection<WindowConfig> first = await sut.GetWindowsAsync();
        IReadOnlyCollection<WindowConfig> second = await sut.GetWindowsAsync();

        Assert.Collection(
            first,
            window =>
            {
                Assert.Equal("0x00000123", window.WindowId);
                Assert.Equal("Terminal", window.WindowTitle);
                Assert.Equal("sample-process", window.ProcessName);
            },
            window => Assert.Equal("0x00000456", window.WindowId)
        );
        Assert.Equal(2, second.Count);
        Assert.Equal(1, resolverCalls);
    }

    [Fact]
    public async Task GetWindowsAsync_EvictsProcessNamesAbsentFromLatestSnapshot()
    {
        uint processId = unchecked((uint)(Environment.ProcessId + 10_001));
        var client = new FakeX11EwmhClient
        {
            Windows = [new X11EwmhWindow(1, processId, "Terminal")],
        };
        int resolverCalls = 0;
        using var sut = new X11EwmhWindowAccessor(
            client,
            (_, _) =>
            {
                resolverCalls++;
                return Task.FromResult("terminal");
            }
        );

        _ = await sut.GetWindowsAsync();
        client.Windows = [];
        _ = await sut.GetWindowsAsync();
        client.Windows = [new X11EwmhWindow(2, processId, "Terminal")];
        _ = await sut.GetWindowsAsync();

        Assert.Equal(2, resolverCalls);
    }

    [Theory]
    [InlineData("0x0000002a", 42u)]
    [InlineData("2A", 42u)]
    [InlineData("0Xffffffff", uint.MaxValue)]
    public async Task TryActivateWindowAsync_ParsesHexWindowId(string windowId, uint expected)
    {
        var client = new FakeX11EwmhClient();
        using var sut = new X11EwmhWindowAccessor(client);

        bool activated = await sut.TryActivateWindowAsync(windowId);

        Assert.True(activated);
        Assert.Equal(expected, client.ActivatedWindowId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x0")]
    [InlineData("not-a-window")]
    public async Task TryActivateWindowAsync_RejectsInvalidWindowId(string windowId)
    {
        var client = new FakeX11EwmhClient();
        using var sut = new X11EwmhWindowAccessor(client);

        bool activated = await sut.TryActivateWindowAsync(windowId);

        Assert.False(activated);
        Assert.Null(client.ActivatedWindowId);
    }

    [Fact]
    public async Task TryRenameWindowAsync_ForwardsUtf8Title()
    {
        var client = new FakeX11EwmhClient();
        using var sut = new X11EwmhWindowAccessor(client);

        bool renamed = await sut.TryRenameWindowAsync("0x0000002a", "Éditeur 日本語");

        Assert.True(renamed);
        Assert.Equal((42u, "Éditeur 日本語"), client.RenamedWindow);
    }

    [Fact]
    public async Task GetWindowsAsync_ObservesCancellation()
    {
        using var sut = new X11EwmhWindowAccessor(new FakeX11EwmhClient());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.GetWindowsAsync(cancellation.Token)
        );
    }

    [Fact]
    public void CreateActiveWindowMessage_UsesPagerSourceAndCurrentWindow()
    {
        XClientMessageEvent message = X11EwmhClient.CreateActiveWindowMessage(
            (IntPtr)1,
            42,
            (IntPtr)2,
            84
        );

        Assert.Equal(X11Native.ClientMessage, message.Type);
        Assert.Equal((IntPtr)42, message.Window);
        Assert.Equal((IntPtr)2, message.MessageType);
        Assert.Equal(32, message.Format);
        Assert.Equal(2, message.Data.Data0);
        Assert.Equal(0, message.Data.Data1);
        Assert.Equal(84, message.Data.Data2);
        Assert.Equal(96, Marshal.SizeOf<XClientMessageEvent>());
    }

    [Fact]
    public void ReadProtocolCardinal_ReadsXlibLongSlotsAs32BitValues()
    {
        IntPtr data = Marshal.AllocHGlobal(IntPtr.Size * 2);
        try
        {
            Marshal.WriteIntPtr(data, 0, unchecked((nint)0x12345678u));
            Marshal.WriteIntPtr(data, IntPtr.Size, unchecked((nint)0xfedcba98u));

            Assert.Equal(0x12345678u, X11EwmhClient.ReadProtocolCardinal(data, 0));
            Assert.Equal(0xfedcba98u, X11EwmhClient.ReadProtocolCardinal(data, 1));
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private sealed class FakeX11EwmhClient : IX11EwmhClient
    {
        public IReadOnlyList<X11EwmhWindow> Windows { get; set; } = [];
        public uint? ActivatedWindowId { get; private set; }
        public (uint WindowId, string Title)? RenamedWindow { get; private set; }

        public IReadOnlyList<X11EwmhWindow> GetWindows() => Windows;

        public bool TryActivateWindow(uint windowId)
        {
            ActivatedWindowId = windowId;
            return true;
        }

        public bool TryRenameWindow(uint windowId, string title)
        {
            RenamedWindow = (windowId, title);
            return true;
        }

        public void Dispose() { }
    }
}
