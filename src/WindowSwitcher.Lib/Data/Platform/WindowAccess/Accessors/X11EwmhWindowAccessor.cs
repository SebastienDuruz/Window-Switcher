using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;

internal sealed class X11EwmhWindowAccessor : WinAccessorBase
{
    private readonly IX11EwmhClient _client;
    private readonly Func<int, CancellationToken, Task<string>> _processNameResolver;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<int, string> _processNames = [];
    private volatile bool _disposed;

    public X11EwmhWindowAccessor()
        : this(new X11EwmhClient(), ReadProcessNameAsync) { }

    internal X11EwmhWindowAccessor(
        IX11EwmhClient client,
        Func<int, CancellationToken, Task<string>>? processNameResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _processNameResolver = processNameResolver ?? ReadProcessNameAsync;
    }

    public override async Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<X11EwmhWindow> nativeWindows = await Task.Run(
                    _client.GetWindows,
                    cancellationToken
                )
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            int currentProcessId = Environment.ProcessId;
            int[] activeProcessIds = nativeWindows
                .Where(window =>
                    window.ProcessId > 0
                    && window.ProcessId <= int.MaxValue
                    && window.ProcessId != currentProcessId
                )
                .Select(window => (int)window.ProcessId)
                .Distinct()
                .ToArray();

            foreach (int staleProcessId in _processNames.Keys.Except(activeProcessIds).ToArray())
                _processNames.Remove(staleProcessId);

            foreach (int processId in activeProcessIds)
            {
                if (_processNames.ContainsKey(processId))
                    continue;

                _processNames[processId] = await _processNameResolver(processId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return nativeWindows
                .Where(window => window.ProcessId != currentProcessId)
                .Select(window =>
                {
                    int processId =
                        window.ProcessId <= int.MaxValue ? (int)window.ProcessId : 0;
                    return new WindowConfig
                    {
                        WindowId = FormatWindowId(window.WindowId),
                        WindowTitle = window.Title,
                        ProcessName = _processNames.GetValueOrDefault(processId, string.Empty),
                    };
                })
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TracePlatformDiagnostics.Instance.Error("X11 EWMH discovery failed", exception);
            return [];
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public override async Task<bool> TryActivateWindowAsync(
        string windowId,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryParseWindowId(windowId, out uint nativeWindowId))
            return false;

        return await ExecuteAsync(
                () => _client.TryActivateWindow(nativeWindowId),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public override async Task<bool> TryRenameWindowAsync(
        string windowId,
        string windowTitle,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(windowTitle);
        if (!TryParseWindowId(windowId, out uint nativeWindowId))
            return false;

        return await ExecuteAsync(
                () => _client.TryRenameWindow(nativeWindowId, windowTitle),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.Dispose();
    }

    internal static string FormatWindowId(uint windowId) => $"0x{windowId:x8}";

    internal static bool TryParseWindowId(string? windowId, out uint nativeWindowId)
    {
        nativeWindowId = 0;
        if (string.IsNullOrWhiteSpace(windowId))
            return false;

        ReadOnlySpan<char> value = windowId.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value[2..];

        return uint.TryParse(
                value,
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out nativeWindowId
            )
            && nativeWindowId != 0;
    }

    private async Task<bool> ExecuteAsync(Func<bool> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool result = await Task.Run(operation, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            TracePlatformDiagnostics.Instance.Error("X11 EWMH operation failed", exception);
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static async Task<string> ReadProcessNameAsync(
        int processId,
        CancellationToken cancellationToken
    )
    {
        if (processId <= 0)
            return string.Empty;

        try
        {
            string processName = await File.ReadAllTextAsync(
                    $"/proc/{processId}/comm",
                    cancellationToken
                )
                .ConfigureAwait(false);
            return processName.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
        )
        {
            return string.Empty;
        }
    }
}
