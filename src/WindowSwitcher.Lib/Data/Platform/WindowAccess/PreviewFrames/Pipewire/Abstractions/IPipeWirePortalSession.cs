using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

[DBusInterface("org.freedesktop.portal.Session")]
internal interface IPipeWirePortalSession : IDBusObject
{
    Task CloseAsync();
}
