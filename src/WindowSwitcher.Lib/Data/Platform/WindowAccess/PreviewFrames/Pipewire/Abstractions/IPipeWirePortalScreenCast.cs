using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

[DBusInterface("org.freedesktop.portal.ScreenCast")]
internal interface IPipeWirePortalScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
    Task<ObjectPath> SelectSourcesAsync(
        ObjectPath sessionHandle,
        IDictionary<string, object> options
    );
    Task<ObjectPath> StartAsync(
        ObjectPath sessionHandle,
        string parentWindow,
        IDictionary<string, object> options
    );
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(
        ObjectPath sessionHandle,
        IDictionary<string, object> options
    );
}
