using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

/// <summary>Defines the D-Bus contract for an XDG Desktop Portal session.</summary>
[DBusInterface("org.freedesktop.portal.Session")]
public interface IPipeWirePortalSession : IDBusObject
{
    /// <summary>Closes the portal session.</summary>
    Task CloseAsync();
}
