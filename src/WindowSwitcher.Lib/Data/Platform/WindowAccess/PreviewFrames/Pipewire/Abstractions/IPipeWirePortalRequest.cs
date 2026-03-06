using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

[DBusInterface("org.freedesktop.portal.Request")]
public interface IPipeWirePortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(
        Action<(uint Response, IDictionary<string, object> Results)> handler
    );
}