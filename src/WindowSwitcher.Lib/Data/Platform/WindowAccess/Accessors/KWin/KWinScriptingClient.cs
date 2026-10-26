using Serilog;
using Tmds.DBus;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin.Abstractions;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;

/// <summary>
/// Loads scripts into KWin and exposes the bridge object they call back.
/// </summary>
internal interface IKWinScriptingClient : IAsyncDisposable
{
    /// <summary>
    /// Connects to the session bus and registers the bridge object once. Calls must not overlap.
    /// </summary>
    /// <returns>The unique bus name scripts must call, or <see langword="null" /> on failure.</returns>
    Task<string?> ConnectAsync(IKWinWindowBridge bridge, CancellationToken cancellationToken);

    /// <summary>
    /// Loads and starts a script, replacing any script already loaded under the same name.
    /// </summary>
    /// <returns><see langword="true" /> when KWin accepted and started the script.</returns>
    Task<bool> LoadAndRunAsync(
        string pluginName,
        string script,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Unloads a script and deletes its source file.
    /// </summary>
    Task UnloadAsync(string pluginName, CancellationToken cancellationToken);
}

internal sealed class KWinScriptingClient(ILogger logger) : IKWinScriptingClient
{
    private const string Destination = "org.kde.KWin";
    private static readonly ObjectPath ScriptingPath = new("/Scripting");
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private readonly object _syncRoot = new();
    private Connection? _connection;
    private string? _localName;
    private bool _disposed;

    public async Task<string?> ConnectAsync(
        IKWinWindowBridge bridge,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(bridge);
        Connection connection;
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A retry after a failed script start reuses the connection and registered object.
            if (_localName is not null)
                return _localName;

            // Exporting an object requires an explicitly connected (non auto-connect) connection.
            connection = new Connection(
                new ClientConnectionOptions(Address.Session)
                {
                    SynchronizationContext = null,
                    AutoConnect = false,
                    RunContinuationsAsynchronously = true,
                }
            );
        }

        try
        {
            ConnectionInfo info = await connection
                .ConnectAsync()
                .WaitAsync(ConnectTimeout, cancellationToken)
                .ConfigureAwait(false);
            await connection
                .RegisterObjectAsync(bridge)
                .WaitAsync(CallTimeout, cancellationToken)
                .ConfigureAwait(false);

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    connection.Dispose();
                    return null;
                }

                _connection = connection;
                _localName = info.LocalName;
                return _localName;
            }
        }
        catch (OperationCanceledException)
        {
            connection.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            connection.Dispose();
            logger.Warning(exception, "KWin bridge registration on the session bus failed");
            return null;
        }
    }

    public async Task<bool> LoadAndRunAsync(
        string pluginName,
        string script,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        if (!OperatingSystem.IsLinux())
            return false;

        Connection connection = GetConnection();
        string? scriptPath = TryGetScriptPath(pluginName);
        if (scriptPath is null)
        {
            logger.Warning("XDG_RUNTIME_DIR is unavailable; KWin scripts cannot be written");
            return false;
        }

        try
        {
            IKWinScripting scripting = connection.CreateProxy<IKWinScripting>(
                Destination,
                ScriptingPath
            );
            bool alreadyLoaded = await CallAsync(
                    scripting.isScriptLoadedAsync(pluginName),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (alreadyLoaded)
                await CallAsync(scripting.unloadScriptAsync(pluginName), cancellationToken)
                    .ConfigureAwait(false);

            Directory.CreateDirectory(
                Path.GetDirectoryName(scriptPath) ?? throw new InvalidOperationException(),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken)
                .ConfigureAwait(false);
            int scriptId = await CallAsync(
                    scripting.loadScriptAsync(scriptPath, pluginName),
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (scriptId < 0)
            {
                logger.Warning("KWin refused to load script {PluginName}", pluginName);
                DeleteScriptFile(scriptPath);
                return false;
            }

            IKWinScript loadedScript = connection.CreateProxy<IKWinScript>(
                Destination,
                new ObjectPath($"/Scripting/Script{scriptId}")
            );
            await CallAsync(loadedScript.runAsync(), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "KWin script {PluginName} could not be started", pluginName);
            return false;
        }
    }

    public async Task UnloadAsync(string pluginName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        Connection? connection;
        lock (_syncRoot)
            connection = _connection;

        try
        {
            if (connection is not null)
            {
                IKWinScripting scripting = connection.CreateProxy<IKWinScripting>(
                    Destination,
                    ScriptingPath
                );
                await CallAsync(scripting.unloadScriptAsync(pluginName), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Debug(exception, "KWin script {PluginName} could not be unloaded", pluginName);
        }
        finally
        {
            if (TryGetScriptPath(pluginName) is { } scriptPath)
                DeleteScriptFile(scriptPath);
        }
    }

    public ValueTask DisposeAsync()
    {
        Connection? connection;
        lock (_syncRoot)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            connection = _connection;
            _connection = null;
        }

        connection?.Dispose();
        return ValueTask.CompletedTask;
    }

    private Connection GetConnection()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _connection
                ?? throw new InvalidOperationException("ConnectAsync must succeed first.");
        }
    }

    private static Task<T> CallAsync<T>(Task<T> call, CancellationToken cancellationToken) =>
        call.WaitAsync(CallTimeout, cancellationToken);

    private static Task CallAsync(Task call, CancellationToken cancellationToken) =>
        call.WaitAsync(CallTimeout, cancellationToken);

    private static string? TryGetScriptPath(string pluginName)
    {
        // KWin reads the file after loadScript returns, so it lives until the script is unloaded.
        // XDG_RUNTIME_DIR is private to the user, unlike the shared temporary directory.
        string? runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Path.IsPathRooted(runtimeDirectory))
            return null;

        return Path.Combine(runtimeDirectory, "window-switcher", $"{pluginName}.js");
    }

    private void DeleteScriptFile(string scriptPath)
    {
        try
        {
            File.Delete(scriptPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Debug(exception, "KWin script file cleanup failed");
        }
    }
}
