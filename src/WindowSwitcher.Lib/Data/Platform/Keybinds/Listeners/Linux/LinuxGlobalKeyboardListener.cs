using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Logging;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Options;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

/// <summary>
/// Linux implementation backed by <see cref="InputEventService"/> from <c>InputEvents.Core</c>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxGlobalKeyboardListener : IGlobalKeyboardListener
{
    private const string PermissionDiagnostic =
        "Reading /dev/input/event* requires elevated access. Use root, add the user to the input group, or configure udev rules.";
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Func<IInputEventService> _serviceFactory;
    private IInputEventService? _service;
    private bool _isDisposed;

    /// <summary>
    /// Creates a listener with default <see cref="InputEventService"/> options for keyboard events.
    /// </summary>
    public LinuxGlobalKeyboardListener()
        : this(CreateDefaultService) { }

    internal LinuxGlobalKeyboardListener(Func<IInputEventService> serviceFactory)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);

        _serviceFactory = serviceFactory;
    }

    /// <inheritdoc />
    public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (IsRunning)
                return;

            IInputEventService service = _serviceFactory();
            service.KeyDown += OnKeyDown;
            service.KeyUp += OnKeyUp;

            try
            {
                await service.StartAsync(cancellationToken).ConfigureAwait(false);
                EnsureKeyboardAccess(service.Devices);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                service.KeyDown -= OnKeyDown;
                service.KeyUp -= OnKeyUp;
                await StopServiceSilentlyAsync(service).ConfigureAwait(false);
                GlobalKeyboardTrace.Error("Linux global keyboard listener failed to start.", ex);
                throw;
            }

            _service = service;
            IsRunning = true;
            GlobalKeyboardTrace.Info("Linux global keyboard listener started.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
                return;

            IInputEventService? service = _service;
            _service = null;
            IsRunning = false;

            if (service is null)
                return;

            service.KeyDown -= OnKeyDown;
            service.KeyUp -= OnKeyUp;
            await service.StopAsync(cancellationToken).ConfigureAwait(false);

            GlobalKeyboardTrace.Info("Linux global keyboard listener stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Linux global keyboard listener shutdown reported a non-fatal error: {ex.Message}"
            );
        }

        KeyEvent = null;
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static void EnsureKeyboardAccess(IReadOnlyList<InputDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        bool hasAccessibleKeyboard = devices.Any(device =>
            device.Kind == DeviceKind.Keyboard && device.IsAccessible
        );
        if (hasAccessibleKeyboard)
            return;

        InputDeviceInfo[] deniedDevices = devices.Where(device => !device.IsAccessible).ToArray();
        if (deniedDevices.Length == 0)
        {
            GlobalKeyboardTrace.Warning(
                "Linux global keyboard listener started but no keyboard device was discovered."
            );
            return;
        }

        string paths = string.Join(", ", deniedDevices.Select(device => device.Path));
        throw new InvalidOperationException(
            $"Global keyboard startup failed on Linux due to inaccessible input devices ({paths}). {PermissionDiagnostic}"
        );
    }

    private static InputEventService CreateDefaultService()
    {
        return new InputEventService(
            new InputEventServiceOptions
            {
                IncludeKinds = [DeviceKind.Keyboard],
                AutoDiscover = true,
                Logger = HandleInputServiceLog,
            }
        );
    }

    private static async Task StopServiceSilentlyAsync(IInputEventService service)
    {
        try
        {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalKeyboardTrace.Warning(
                $"Linux input service cleanup reported a non-fatal error: {ex.Message}"
            );
        }
    }

    private static void HandleInputServiceLog(
        InputLogLevel level,
        string message,
        Exception? exception
    )
    {
        string logMessage = $"Linux InputEvents.Core: {message}";
        switch (level)
        {
            case InputLogLevel.Debug:
                GlobalKeyboardTrace.Debug(logMessage);
                break;
            case InputLogLevel.Info:
                GlobalKeyboardTrace.Info(logMessage);
                break;
            case InputLogLevel.Warn:
                GlobalKeyboardTrace.Warning(logMessage);
                break;
            case InputLogLevel.Error:
                GlobalKeyboardTrace.Error(logMessage, exception);
                return;
            default:
                GlobalKeyboardTrace.Debug(logMessage);
                break;
        }

        if (exception is not null && level >= InputLogLevel.Warn)
            GlobalKeyboardTrace.Error(logMessage, exception);
    }

    private void OnKeyDown(object? sender, KeyEvent keyEvent)
    {
        Emit(GlobalKeyboardEventMapper.FromLinux(keyEvent, GlobalKeyState.Down, isRepeat: false));
    }

    private void OnKeyUp(object? sender, KeyEvent keyEvent)
    {
        Emit(GlobalKeyboardEventMapper.FromLinux(keyEvent, GlobalKeyState.Up, isRepeat: false));
    }

    private void Emit(GlobalKeyEventArgs eventArgs)
    {
        Delegate[] subscribers = KeyEvent?.GetInvocationList() ?? [];
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((EventHandler<GlobalKeyEventArgs>)subscriber).Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                GlobalKeyboardTrace.Warning(
                    $"Global keyboard subscriber failed for key {eventArgs.KeyCode}: {ex.Message}"
                );
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(LinuxGlobalKeyboardListener));
    }
}
