using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

[SupportedOSPlatform("linux")]
public sealed class LinuxGlobalKeyboardListenerTests
{
    [Fact]
    public void EnsureKeyboardAccess_DoesNotThrow_WhenAccessibleKeyboardExists()
    {
        var devices = new[]
        {
            new InputDeviceInfo
            {
                Path = "/dev/input/event4",
                Kind = DeviceKind.Keyboard,
                IsAccessible = true,
            },
        };

        LinuxGlobalKeyboardListener.EnsureKeyboardAccess(devices);
    }

    [Fact]
    public void EnsureKeyboardAccess_ThrowsDiagnostic_WhenDevicesAreInaccessible()
    {
        var devices = new[]
        {
            new InputDeviceInfo
            {
                Path = "/dev/input/event0",
                IsAccessible = false,
                AccessError = "Permission denied",
            },
            new InputDeviceInfo
            {
                Path = "/dev/input/event1",
                IsAccessible = false,
                AccessError = "Permission denied",
            },
        };

        LinuxInputAccessException exception = Assert.Throws<LinuxInputAccessException>(() =>
            LinuxGlobalKeyboardListener.EnsureKeyboardAccess(devices)
        );

        Assert.Contains("/dev/input/event*", exception.Message, StringComparison.Ordinal);
        Assert.Contains("input group", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["/dev/input/event0", "/dev/input/event1"], exception.DevicePaths);
    }

    [Fact]
    public void SelectKeyboardDevices_ExcludesProjectVirtualKeyboardByStableIdentity()
    {
        InputDeviceInfo physical = CreateKeyboard("/dev/input/event1", [30]);
        InputDeviceInfo virtualKeyboard = CreateKeyboard("/dev/input/event2", [30]) with
        {
            Name = LinuxUinputKeyboardForwarder.DeviceName,
            VendorId = LinuxUinputKeyboardForwarder.VendorId,
            ProductId = LinuxUinputKeyboardForwarder.ProductId,
        };

        InputDeviceInfo[] selected = LinuxGlobalKeyboardListener.SelectKeyboardDevices(
            [virtualKeyboard, physical]
        );

        Assert.Equal([physical], selected);
    }

    [Fact]
    public void DeviceSignature_ChangesWhenCapabilitiesChangeButNotOrder()
    {
        InputDeviceInfo first = CreateKeyboard("/dev/input/event1", [30, 31]);
        InputDeviceInfo second = CreateKeyboard("/dev/input/event2", [32]);
        InputDeviceInfo changed = first with
        {
            Caps = new InputDeviceCapabilities(keyCodes: [30, 31, 33]),
        };

        string ordered = LinuxGlobalKeyboardListener.BuildDeviceSignature([first, second]);
        string reversed = LinuxGlobalKeyboardListener.BuildDeviceSignature([second, first]);
        string changedSignature = LinuxGlobalKeyboardListener.BuildDeviceSignature(
            [changed, second]
        );

        Assert.Equal(ordered, reversed);
        Assert.NotEqual(ordered, changedSignature);
    }

    [Fact]
    public void UinputCapabilities_AreTheUnionOfPhysicalKeyboards()
    {
        InputDeviceInfo first = CreateKeyboard("/dev/input/event1", [0, 30, 31]);
        InputDeviceInfo second = CreateKeyboard("/dev/input/event2", [31, 32, 0x300]);

        IReadOnlyCollection<ushort> keyCodes =
            LinuxUinputKeyboardForwarder.CollectKeyCodes([first, second]);

        Assert.Equal([30, 31, 32], keyCodes);
    }

    [Fact]
    public async Task StartWithoutKeyboard_RemainsRunningAndDoesNotCreateUinput()
    {
        var discovery = new SequenceDiscovery([]);
        var forwarders = new RecordingForwarderFactory();
        await using var listener = CreateListener(discovery, forwarders, TimeSpan.FromHours(1));

        await listener.StartAsync();

        Assert.True(listener.IsRunning);
        Assert.Empty(forwarders.Created);
        await listener.StopAsync();
    }

    [Fact]
    public async Task Start_PropagatesCategorizedUinputFailure()
    {
        InputDeviceInfo keyboard = CreateKeyboard("/dev/input/event1", [30]);
        var failure = new LinuxUinputAccessException(
            LinuxUinputFailureKind.Missing,
            "uinput is missing"
        );
        await using var listener = CreateListener(
            new SequenceDiscovery([keyboard]),
            new FailingForwarderFactory(failure),
            TimeSpan.FromHours(1)
        );

        LinuxUinputAccessException actual = await Assert.ThrowsAsync<LinuxUinputAccessException>(
            () => listener.StartAsync()
        );

        Assert.Same(failure, actual);
        Assert.Equal(LinuxUinputFailureKind.Missing, actual.FailureKind);
        Assert.False(listener.IsRunning);
    }

    [Fact]
    public async Task StartCancellation_CleansUpPartiallyStartedPipeline()
    {
        InputDeviceInfo keyboard = CreateKeyboard("/dev/input/event1", [30]);
        await using var listener = CreateListener(
            new SequenceDiscovery([keyboard]),
            new CancelingForwarderFactory(),
            TimeSpan.FromHours(1)
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            listener.StartAsync(cancellation.Token)
        );

        Assert.False(listener.IsRunning);
    }

    [Fact]
    public async Task Reconciliation_RebuildsUinputForHotplugAndCapabilityChanges()
    {
        InputDeviceInfo first = CreateKeyboard("/definitely/missing-event1", [30]);
        InputDeviceInfo changed = first with
        {
            Caps = new InputDeviceCapabilities(keyCodes: [30, 31]),
        };
        var discovery = new SequenceDiscovery([], [first], [changed], []);
        var forwarders = new RecordingForwarderFactory(expectedDisposals: 2);
        await using var listener = CreateListener(
            discovery,
            forwarders,
            TimeSpan.FromMilliseconds(10)
        );

        await listener.StartAsync();
        await forwarders.AllDisposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, forwarders.Created.Count);
        Assert.All(forwarders.Created, forwarder => Assert.True(forwarder.IsDisposed));
        await listener.StopAsync();
    }

    [Fact]
    public async Task Routing_PreservesPerDeviceSyncAndConsumesOnlyTargetDevice()
    {
        InputDeviceInfo keyboard = CreateKeyboard("/definitely/missing-event1", [30]);
        var forwarders = new RecordingForwarderFactory();
        await using var listener = CreateListener(
            new SequenceDiscovery([keyboard]),
            forwarders,
            TimeSpan.FromHours(1)
        );
        listener.InputFilter = new DeviceFilter("device-b");
        await listener.StartAsync();
        RecordingForwarder forwarder = Assert.Single(forwarders.Created);

        listener.ProcessNativeEventForTesting("device-a", KeyDown());
        listener.ProcessNativeEventForTesting("device-b", KeyDown());
        listener.ProcessNativeEventForTesting("device-b", Sync());
        listener.ProcessNativeEventForTesting("device-a", Sync());

        Assert.Collection(
            forwarder.Events,
            nativeEvent => Assert.Equal(LinuxInputConstants.EvKey, nativeEvent.Type),
            nativeEvent => Assert.Equal(LinuxInputConstants.EvSyn, nativeEvent.Type)
        );
        await listener.StopAsync();
    }

    [Fact]
    public async Task Routing_FlushesBufferedShortcutPrefixBeforeCurrentEventAndSync()
    {
        InputDeviceInfo keyboard = CreateKeyboard("/definitely/missing-event1", [30]);
        var forwarders = new RecordingForwarderFactory();
        await using var listener = CreateListener(
            new SequenceDiscovery([keyboard]),
            forwarders,
            TimeSpan.FromHours(1)
        );
        listener.InputFilter = new SequenceFilter(
            KeyboardFilterDecision.Buffer(),
            KeyboardFilterDecision.Forward(flushBufferedEvents: true)
        );
        await listener.StartAsync();
        RecordingForwarder forwarder = Assert.Single(forwarders.Created);

        listener.ProcessNativeEventForTesting("device-a", KeyDown());
        listener.ProcessNativeEventForTesting("device-a", KeyDown());
        listener.ProcessNativeEventForTesting("device-a", Sync());

        Assert.Collection(
            forwarder.Events,
            first => Assert.Equal(LinuxInputConstants.EvKey, first.Type),
            second => Assert.Equal(LinuxInputConstants.EvKey, second.Type),
            sync => Assert.Equal(LinuxInputConstants.EvSyn, sync.Type)
        );
        await listener.StopAsync();
    }

    private static LinuxGlobalKeyboardListener CreateListener(
        ILinuxInputDeviceDiscovery discovery,
        ILinuxKeyboardForwarderFactory forwarderFactory,
        TimeSpan reconciliationInterval
    ) =>
        new(
            discovery,
            forwarderFactory,
            new RecordingDiagnostics(),
            reconciliationInterval
        );

    private static InputDeviceInfo CreateKeyboard(string path, ushort[] keyCodes) =>
        new()
        {
            Path = path,
            Kind = DeviceKind.Keyboard,
            IsAccessible = true,
            Caps = new InputDeviceCapabilities(keyCodes: keyCodes),
        };

    private static NativeInputEvent KeyDown() =>
        new()
        {
            Type = LinuxInputConstants.EvKey,
            Code = LinuxInputConstants.KeyA,
            Value = 1,
        };

    private static NativeInputEvent Sync() =>
        new() { Type = LinuxInputConstants.EvSyn };

    private sealed class SequenceDiscovery(params IReadOnlyList<InputDeviceInfo>[] snapshots)
        : ILinuxInputDeviceDiscovery
    {
        private int _index;

        public Task<IReadOnlyList<InputDeviceInfo>> DiscoverAsync(
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Interlocked.Increment(ref _index) - 1;
            IReadOnlyList<InputDeviceInfo> snapshot = snapshots.Length == 0
                ? []
                : snapshots[Math.Min(index, snapshots.Length - 1)];
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingForwarderFactory(int expectedDisposals = 0)
        : ILinuxKeyboardForwarderFactory
    {
        private int _disposeCount;
        internal List<RecordingForwarder> Created { get; } = [];
        internal TaskCompletionSource AllDisposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ILinuxKeyboardForwarder> CreateAsync(
            IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forwarder = new RecordingForwarder(() =>
            {
                if (Interlocked.Increment(ref _disposeCount) >= expectedDisposals)
                    AllDisposed.TrySetResult();
            });
            Created.Add(forwarder);
            return Task.FromResult<ILinuxKeyboardForwarder>(forwarder);
        }
    }

    private sealed class FailingForwarderFactory(Exception failure)
        : ILinuxKeyboardForwarderFactory
    {
        public Task<ILinuxKeyboardForwarder> CreateAsync(
            IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
            CancellationToken cancellationToken
        ) => Task.FromException<ILinuxKeyboardForwarder>(failure);
    }

    private sealed class CancelingForwarderFactory : ILinuxKeyboardForwarderFactory
    {
        public async Task<ILinuxKeyboardForwarder> CreateAsync(
            IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellable wait unexpectedly completed.");
        }
    }

    private sealed class RecordingForwarder(Action onDispose) : ILinuxKeyboardForwarder
    {
        internal List<NativeInputEvent> Events { get; } = [];
        internal bool IsDisposed { get; private set; }

        public void Forward(NativeInputEvent nativeEvent) => Events.Add(nativeEvent);

        public void Forward(IEnumerable<NativeInputEvent> nativeEvents) =>
            Events.AddRange(nativeEvents);

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            onDispose();
        }
    }

    private sealed class DeviceFilter(string consumedDevice) : IKeyboardInputFilter
    {
        public KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent) =>
            string.Equals(keyEvent.DeviceId, consumedDevice, StringComparison.Ordinal)
                ? KeyboardFilterDecision.Consume()
                : KeyboardFilterDecision.Forward();

        public void Reset() { }
    }

    private sealed class SequenceFilter(params KeyboardFilterDecision[] decisions)
        : IKeyboardInputFilter
    {
        private int _index;

        public KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent)
        {
            int index = Interlocked.Increment(ref _index) - 1;
            return decisions[Math.Min(index, decisions.Length - 1)];
        }

        public void Reset() => _index = 0;
    }

    private sealed class RecordingDiagnostics : IPlatformDiagnostics
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
