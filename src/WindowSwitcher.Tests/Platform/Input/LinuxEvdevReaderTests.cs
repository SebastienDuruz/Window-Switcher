using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog.Events;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Tests.TestLogging;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

/// <summary>
/// Exercises the evdev reader loop of <see cref="LinuxGlobalKeyboardListener"/> against scripted
/// <c>open</c>/<c>ioctl</c>/<c>read</c> results, covering the kernel behaviours that only show up
/// on real hardware: short reads, interrupted calls, unplugged devices and refused grabs.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxEvdevReaderTests
{
    private const string DevicePath = "/dev/input/event-test";
    private const ushort KeyB = 48;
    private const int Ebadf = 9;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EventsSplitAcrossReads_AreReassembled()
    {
        byte[] stream = Bytes(Key(LinuxInputConstants.KeyA, 1), Key(KeyB, 1));
        var evdev = new ScriptedEvdev();
        evdev.EnqueueRead(stream[..10]);
        evdev.EnqueueRead(stream[10..30]);
        evdev.EnqueueRead(stream[30..]);
        await using var harness = await Harness.StartAsync(evdev);

        await harness.WaitForKeyEventsAsync(2);

        Assert.Equal(["KEY_30", "KEY_48"], harness.KeyCodes);
    }

    [Fact]
    public async Task InterruptedAndWouldBlockReads_AreRetriedOnTheSameDevice()
    {
        var evdev = new ScriptedEvdev();
        evdev.EnqueueReadError(LinuxNative.Eintr);
        evdev.EnqueueReadError(LinuxNative.Eagain);
        evdev.EnqueueRead(Bytes(Key(LinuxInputConstants.KeyA, 1)));
        await using var harness = await Harness.StartAsync(evdev);

        await harness.WaitForKeyEventsAsync(1);

        Assert.Equal(1, evdev.OpenCount);
        Assert.Empty(evdev.Closed);
    }

    [Theory]
    [InlineData(0)] // end of file: the device node went away
    [InlineData(LinuxNative.Enodev)]
    [InlineData(LinuxNative.Eio)]
    [InlineData(Ebadf)] // unexpected failure: logged, then the device is reopened
    public async Task FailedRead_ReleasesGrabClosesAndReopensTheDevice(int errno)
    {
        var evdev = new ScriptedEvdev();
        if (errno == 0)
            evdev.EnqueueEndOfFile();
        else
            evdev.EnqueueReadError(errno);
        await using var harness = await Harness.StartAsync(evdev);

        await WaitUntilAsync(() => evdev.OpenCount >= 2);

        Assert.Equal(ScriptedEvdev.FirstDescriptor, evdev.Closed.First());
        Assert.Contains((ScriptedEvdev.FirstDescriptor, false), evdev.GrabChanges);
        Assert.Equal(errno == Ebadf, harness.Logs.Contains(LogEventLevel.Error, "evdev reader"));
    }

    [Fact]
    public async Task MissingDevice_IsRetriedUntilItAppears()
    {
        var evdev = new ScriptedEvdev();
        evdev.EnqueueOpenError(LinuxNative.Enoent);
        evdev.EnqueueOpenError(LinuxNative.Enoent);
        evdev.EnqueueRead(Bytes(Key(LinuxInputConstants.KeyA, 1)));
        await using var harness = await Harness.StartAsync(evdev);

        await harness.WaitForKeyEventsAsync(1);

        Assert.Equal(3, evdev.OpenCount);
    }

    [Fact]
    public async Task RefusedOpen_StopsReadingWithoutRetrying()
    {
        var evdev = new ScriptedEvdev();
        evdev.EnqueueOpenError(LinuxNative.Eacces);
        await using var harness = await Harness.StartAsync(evdev);

        await WaitUntilAsync(() => harness.Logs.Contains(LogEventLevel.Warning, "was refused"));
        await Task.Delay(50);

        Assert.Equal(1, evdev.OpenCount);
    }

    [Fact]
    public async Task RefusedGrab_ClosesTheDeviceWithoutRetrying()
    {
        var evdev = new ScriptedEvdev();
        evdev.EnqueueGrabError(LinuxNative.Eperm);
        await using var harness = await Harness.StartAsync(evdev);

        await WaitUntilAsync(() => evdev.Closed.Count == 1);
        await Task.Delay(50);

        Assert.Equal(1, evdev.OpenCount);
        Assert.True(harness.Logs.Contains(LogEventLevel.Warning, "Grabbing Linux evdev device"));
        Assert.DoesNotContain((ScriptedEvdev.FirstDescriptor, false), evdev.GrabChanges);
    }

    [Fact]
    public async Task Stop_ReleasesGrabAndClosesTheDevice()
    {
        var evdev = new ScriptedEvdev();
        var harness = await Harness.StartAsync(evdev);
        await WaitUntilAsync(() => evdev.GrabChanges.Count == 1);

        await harness.DisposeAsync();

        Assert.Equal(
            [(ScriptedEvdev.FirstDescriptor, true), (ScriptedEvdev.FirstDescriptor, false)],
            evdev.GrabChanges
        );
        Assert.Equal([ScriptedEvdev.FirstDescriptor], evdev.Closed);
    }

    private static NativeInputEvent Key(ushort code, int value) =>
        new()
        {
            Type = LinuxInputConstants.EvKey,
            Code = code,
            Value = value,
        };

    private static byte[] Bytes(params NativeInputEvent[] events) =>
        MemoryMarshal.AsBytes(events.AsSpan()).ToArray();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(WaitTimeout);
        while (!condition())
            await Task.Delay(5, timeout.Token);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly LinuxGlobalKeyboardListener _listener;
        private readonly ConcurrentQueue<GlobalKeyEventArgs> _keyEvents = new();

        private Harness(LinuxGlobalKeyboardListener listener, CollectingSink logs)
        {
            _listener = listener;
            Logs = logs;
            _listener.KeyEvent += (_, keyEvent) => _keyEvents.Enqueue(keyEvent);
        }

        public CollectingSink Logs { get; }

        public string[] KeyCodes => _keyEvents.Select(keyEvent => keyEvent.KeyCode).ToArray();

        public static async Task<Harness> StartAsync(ScriptedEvdev evdev)
        {
            var listener = new LinuxGlobalKeyboardListener(
                new SingleKeyboardDiscovery(),
                new NoOpForwarderFactory(),
                TestLogger.Create(out CollectingSink logs),
                TimeSpan.FromHours(1),
                evdev,
                reconnectDelay: TimeSpan.FromMilliseconds(1)
            );
            var harness = new Harness(listener, logs);
            await listener.StartAsync();
            return harness;
        }

        public Task WaitForKeyEventsAsync(int count) =>
            WaitUntilAsync(() => _keyEvents.Count >= count);

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync();
            await _listener.DisposeAsync();
        }
    }

    private sealed class ScriptedEvdev : ILinuxEvdevIo
    {
        public const int FirstDescriptor = 100;
        private readonly object _sync = new();
        private readonly Queue<int> _openErrors = new();
        private readonly Queue<int> _grabErrors = new();
        private readonly Queue<(byte[]? Data, int Errno)> _reads = new();
        private readonly List<(int Descriptor, bool Grabbed)> _grabChanges = [];
        private readonly List<int> _closed = [];
        private int _openCount;
        private int _successfulOpens;

        public int OpenCount
        {
            get
            {
                lock (_sync)
                    return _openCount;
            }
        }

        public IReadOnlyList<(int Descriptor, bool Grabbed)> GrabChanges
        {
            get
            {
                lock (_sync)
                    return _grabChanges.ToArray();
            }
        }

        public IReadOnlyList<int> Closed
        {
            get
            {
                lock (_sync)
                    return _closed.ToArray();
            }
        }

        public void EnqueueOpenError(int errno) => _openErrors.Enqueue(errno);

        public void EnqueueGrabError(int errno) => _grabErrors.Enqueue(errno);

        public void EnqueueRead(byte[] data) => _reads.Enqueue((data, 0));

        public void EnqueueReadError(int errno) => _reads.Enqueue((null, errno));

        public void EnqueueEndOfFile() => _reads.Enqueue(([], 0));

        public int Open(string devicePath, out int errno)
        {
            lock (_sync)
            {
                _openCount++;
                if (_openErrors.TryDequeue(out errno))
                    return -1;
                errno = 0;
                // Descriptors are numbered by successful opens, so the first one is always 100.
                return FirstDescriptor + _successfulOpens++;
            }
        }

        public int SetGrab(int fileDescriptor, bool grabbed, out int errno)
        {
            lock (_sync)
            {
                if (grabbed && _grabErrors.TryDequeue(out errno))
                    return -1;
                _grabChanges.Add((fileDescriptor, grabbed));
                errno = 0;
                return 0;
            }
        }

        public nint Read(int fileDescriptor, byte[] buffer, out int errno)
        {
            lock (_sync)
            {
                if (!_reads.TryDequeue(out (byte[]? Data, int Errno) step))
                {
                    errno = LinuxNative.Eagain;
                    return -1;
                }

                errno = step.Errno;
                if (step.Data is null)
                    return -1;

                step.Data.CopyTo(buffer, 0);
                return step.Data.Length;
            }
        }

        public void Close(int fileDescriptor)
        {
            lock (_sync)
                _closed.Add(fileDescriptor);
        }
    }

    private sealed class SingleKeyboardDiscovery : ILinuxInputDeviceDiscovery
    {
        public Task<IReadOnlyList<InputDeviceInfo>> DiscoverAsync(
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<InputDeviceInfo>>([
                new InputDeviceInfo
                {
                    Path = DevicePath,
                    Kind = DeviceKind.Keyboard,
                    IsAccessible = true,
                    Caps = new InputDeviceCapabilities(keyCodes: [LinuxInputConstants.KeyA]),
                },
            ]);
    }

    private sealed class NoOpForwarderFactory : ILinuxKeyboardForwarderFactory
    {
        public Task<ILinuxKeyboardForwarder> CreateAsync(
            IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
            CancellationToken cancellationToken
        ) => Task.FromResult<ILinuxKeyboardForwarder>(new NoOpForwarder());
    }

    private sealed class NoOpForwarder : ILinuxKeyboardForwarder
    {
        public void Forward(NativeInputEvent nativeEvent) { }

        public void Dispose() { }
    }
}
