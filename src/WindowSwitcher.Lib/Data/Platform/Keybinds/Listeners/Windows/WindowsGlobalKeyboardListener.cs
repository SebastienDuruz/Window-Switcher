using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
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
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkhfExtended = 0x00000001;
    private const uint LlkhfInjected = 0x00000010;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _pressedKeysSync = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private HookProc? _hookProc;
    private TaskCompletionSource<object?>? _startTcs;
    private TaskCompletionSource<object?>? _stopTcs;
    private bool _isDisposed;

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
        catch (Exception) { }

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
            _ = Marshal.GetLastWin32Error();

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
                    _ = Marshal.GetLastWin32Error();
                    break;
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception) { }
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
            uint message = unchecked((uint)wParam.ToInt64());
            bool isDown = message == WmKeyDown || message == WmSysKeyDown;
            bool isUp = message == WmKeyUp || message == WmSysKeyUp;
            if (!isDown && !isUp)
                return CallNextHookEx(_hookHandle, code, wParam, lParam);

            KeyboardHookData keyboardData = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            if ((keyboardData.Flags & LlkhfInjected) != 0)
                return CallNextHookEx(_hookHandle, code, wParam, lParam);

            GlobalKeyState state = isDown ? GlobalKeyState.Down : GlobalKeyState.Up;
            bool isRepeat = false;

            lock (_pressedKeysSync)
            {
                if (state == GlobalKeyState.Down)
                    isRepeat = !_pressedKeys.Add(keyboardData.VirtualKeyCode);
                else
                    _pressedKeys.Remove(keyboardData.VirtualKeyCode);
            }

            string? keyName = ResolveKeyName(keyboardData);
            var eventArgs = GlobalKeyboardEventMapper.FromWindows(
                keyboardData.VirtualKeyCode,
                keyName,
                state,
                isRepeat
            );

            KeyboardFilterDecision decision = EvaluateFilter(eventArgs);
            Emit(eventArgs);
            return ApplyDecision(decision, code, wParam, lParam);
        }
        catch (Exception)
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
    }

    private KeyboardFilterDecision EvaluateFilter(GlobalKeyEventArgs eventArgs)
    {
        IKeyboardInputFilter? filter = InputFilter;
        if (filter is null)
            return KeyboardFilterDecision.Forward();

        try
        {
            return filter.ProcessEvent(eventArgs);
        }
        catch (Exception)
        {
            return KeyboardFilterDecision.Forward();
        }
    }

    private IntPtr ApplyDecision(
        KeyboardFilterDecision decision,
        int code,
        IntPtr wParam,
        IntPtr lParam
    )
    {
        return decision.Routing == KeyboardEventRouting.Consume
            ? new IntPtr(1)
            : CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static string? ResolveKeyName(KeyboardHookData keyboardData)
    {
        uint scanCode = keyboardData.ScanCode;
        if ((keyboardData.Flags & LlkhfExtended) != 0)
            scanCode |= 0xE000;

        int lParam = unchecked((int)(scanCode << 16));
        var keyNameBuilder = new StringBuilder(64);
        int length = GetKeyNameText(lParam, keyNameBuilder, keyNameBuilder.Capacity);
        if (length <= 0)
            return null;

        return keyNameBuilder.ToString();
    }

    private void CleanupHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            if (!UnhookWindowsHookEx(_hookHandle))
                _ = Marshal.GetLastWin32Error();

            _hookHandle = IntPtr.Zero;
        }

        lock (_pressedKeysSync)
        {
            _pressedKeys.Clear();
        }

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
            catch (Exception) { }
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
