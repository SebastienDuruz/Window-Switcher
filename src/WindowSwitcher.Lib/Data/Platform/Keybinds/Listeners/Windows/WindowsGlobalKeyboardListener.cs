using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Serilog;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;

/// <summary>
/// Windows implementation based on a low-level global keyboard hook (<c>WH_KEYBOARD_LL</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalKeyboardListener : IGlobalKeyboardListener
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const uint WmQuit = 0x0012;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly WindowsKeyboardHookProcessor _processor;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private HookProc? _hookProc;
    private TaskCompletionSource<object?>? _startTcs;
    private TaskCompletionSource<object?>? _stopTcs;
    private readonly ILogger _logger;
    private bool _isDisposed;

    /// <summary>
    /// Creates the Windows low-level keyboard hook listener.
    /// </summary>
    public WindowsGlobalKeyboardListener()
        : this(Log.ForContext<WindowsGlobalKeyboardListener>()) { }

    internal WindowsGlobalKeyboardListener(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _processor = new WindowsKeyboardHookProcessor(ResolveKeyName, logger);
    }

    /// <inheritdoc />
    public IKeyboardInputFilter? InputFilter { get; set; }

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

            _startTcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _stopTcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "WindowSwitcher-GlobalKeyboardHook",
            };
            _hookThread.Start();

            await _startTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            IsRunning = true;
        }
        catch
        {
            await StopThreadCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
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

            await StopThreadCoreAsync(cancellationToken).ConfigureAwait(false);
            IsRunning = false;
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
        catch (Exception exception)
        {
            // Best-effort shutdown: disposal must complete even if the hook thread did not stop.
            _logger.Debug(exception, "Windows keyboard listener shutdown failed during disposal");
        }

        KeyEvent = null;
        InputFilter = null;
        _lifecycleGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task StopThreadCoreAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<object?>? stopTcs = _stopTcs;
        uint threadId = _hookThreadId;

        bool quitPosted = threadId == 0;
        for (int attempt = 0; !quitPosted && attempt < 5; attempt++)
        {
            quitPosted = PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            if (quitPosted)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (!quitPosted)
        {
            _logger.Warning(
                "Posting WM_QUIT to the Windows keyboard hook thread failed (Win32={Win32Error})",
                Marshal.GetLastWin32Error()
            );
        }

        if (stopTcs is not null)
            await stopTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        _startTcs = null;
        _stopTcs = null;
        _hookThread = null;
        IsRunning = false;
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        _hookProc = HookCallback;

        IntPtr moduleHandle = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            var exception = new InvalidOperationException(
                $"SetWindowsHookEx(WH_KEYBOARD_LL) failed (Win32={error})."
            );
            _startTcs?.TrySetException(exception);
            _stopTcs?.TrySetResult(null);
            return;
        }

        _ = PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _startTcs?.TrySetResult(null);

        try
        {
            while (true)
            {
                int messageResult = GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0);
                if (messageResult == 0)
                    break;

                if (messageResult < 0)
                {
                    _logger.Error(
                        "Windows keyboard hook message loop failed (Win32={Win32Error})",
                        Marshal.GetLastWin32Error()
                    );
                    break;
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Windows keyboard hook message loop stopped unexpectedly");
        }
        finally
        {
            CleanupHook();
            _stopTcs?.TrySetResult(null);
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code != HcAction)
            return CallNextHookEx(_hookHandle, code, wParam, lParam);

        try
        {
            KeyboardHookData keyboardData = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            GlobalKeyEventArgs? eventArgs = _processor.TryCreateEvent(
                unchecked((uint)wParam.ToInt64()),
                keyboardData.VirtualKeyCode,
                keyboardData.ScanCode,
                keyboardData.Flags
            );
            if (eventArgs is null)
                return CallNextHookEx(_hookHandle, code, wParam, lParam);

            bool consume = _processor.ShouldConsume(eventArgs, InputFilter);
            Emit(eventArgs);
            return consume ? new IntPtr(1) : CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Windows keyboard hook callback failed");
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
    }

    private static string? ResolveKeyName(int keyNameParameter)
    {
        var keyNameBuilder = new StringBuilder(64);
        int length = GetKeyNameText(keyNameParameter, keyNameBuilder, keyNameBuilder.Capacity);
        return length > 0 ? keyNameBuilder.ToString() : null;
    }

    private void CleanupHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            if (!UnhookWindowsHookEx(_hookHandle))
            {
                _logger.Warning(
                    "Removing the Windows keyboard hook failed (Win32={Win32Error})",
                    Marshal.GetLastWin32Error()
                );
            }

            _hookHandle = IntPtr.Zero;
        }

        _processor.Reset();

        _hookThreadId = 0;
        _hookProc = null;
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
            catch (Exception exception)
            {
                _logger.Error(exception, "Windows keyboard event subscriber failed");
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(WindowsGlobalKeyboardListener));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc callback,
        IntPtr moduleHandle,
        uint threadId
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minMessage,
        uint maxMessage
    );

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr windowHandle,
        uint minMessage,
        uint maxMessage,
        uint removeMessage
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder keyName, int size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
