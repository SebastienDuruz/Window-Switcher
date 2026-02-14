using Tmds.DBus;

namespace WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames;

[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IPipeWirePortalScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectSourcesAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
    Task<ObjectPath> StartAsync(ObjectPath sessionHandle, string parentWindow, IDictionary<string, object> options);
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(ObjectPath sessionHandle, IDictionary<string, object> options);
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IPipeWirePortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint Response, IDictionary<string, object> Results)> handler);
}

[DBusInterface("org.freedesktop.portal.Session")]
public interface IPipeWirePortalSession : IDBusObject
{
    Task CloseAsync();
}

[DBusInterface("org.freedesktop.impl.portal.ScreenCast")]
public interface IKdePortalScreenCast : IDBusObject
{
    Task<(uint Response, IDictionary<string, object> Results)> CreateSessionAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        IDictionary<string, object> options);

    Task<(uint Response, IDictionary<string, object> Results)> SelectSourcesAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        IDictionary<string, object> options);

    Task<(uint Response, IDictionary<string, object> Results)> StartAsync(
        ObjectPath handle,
        ObjectPath sessionHandle,
        string appId,
        string parentWindow,
        IDictionary<string, object> options);
}

[DBusInterface("org.freedesktop.impl.portal.Session")]
public interface IKdePortalSession : IDBusObject
{
    Task CloseAsync();
}
