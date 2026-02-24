using Tmds.DBus;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

[DBusInterface("org.freedesktop.impl.portal.ScreenCast")]
public interface IKdePortalScreenCast : IDBusObject
{
    Task<(uint Response, IDictionary<string, object> Results)> CreateSessionAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        IDictionary<string, object> options
    );

    Task<(uint Response, IDictionary<string, object> Results)> SelectSourcesAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        IDictionary<string, object> options
    );

    Task<(uint Response, IDictionary<string, object> Results)> StartAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        string parentWindow,
        IDictionary<string, object> options
    );
}