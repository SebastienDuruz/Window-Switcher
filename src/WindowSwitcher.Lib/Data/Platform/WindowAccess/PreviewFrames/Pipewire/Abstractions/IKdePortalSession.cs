using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

[DBusInterface("org.freedesktop.impl.portal.Session")]
public interface IKdePortalSession : IDBusObject
{
    Task CloseAsync();
}