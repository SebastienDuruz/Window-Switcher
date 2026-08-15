using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

/// <summary>Defines the D-Bus contract for an XDG Desktop Portal request.</summary>
[DBusInterface("org.freedesktop.portal.Request")]
public interface IPipeWirePortalRequest : IDBusObject
{
    /// <summary>Watches the response emitted when the portal request completes.</summary>
    /// <param name="handler">Callback receiving the response code and result values.</param>
    /// <returns>A disposable response-signal subscription.</returns>
    Task<IDisposable> WatchResponseAsync(
        Action<(uint Response, IDictionary<string, object> Results)> handler
    );
}
