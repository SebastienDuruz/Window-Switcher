using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire.Abstractions;

/// <summary>Defines the D-Bus contract for the XDG ScreenCast portal.</summary>
[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IPipeWirePortalScreenCast : IDBusObject
{
    /// <summary>Creates a portal ScreenCast session.</summary>
    /// <param name="options">Portal request options.</param>
    /// <returns>The request object path.</returns>
    Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);

    /// <summary>Selects the sources exposed by the session.</summary>
    /// <param name="sessionHandle">Portal session object path.</param>
    /// <param name="options">Source-selection options.</param>
    /// <returns>The request object path.</returns>
    Task<ObjectPath> SelectSourcesAsync(
        ObjectPath sessionHandle,
        IDictionary<string, object> options
    );

    /// <summary>Starts streaming the selected sources.</summary>
    /// <param name="sessionHandle">Portal session object path.</param>
    /// <param name="parentWindow">Optional parent-window identifier.</param>
    /// <param name="options">Start request options.</param>
    /// <returns>The request object path.</returns>
    Task<ObjectPath> StartAsync(
        ObjectPath sessionHandle,
        string parentWindow,
        IDictionary<string, object> options
    );

    /// <summary>Opens the PipeWire remote associated with the session.</summary>
    /// <param name="sessionHandle">Portal session object path.</param>
    /// <param name="options">PipeWire remote options.</param>
    /// <returns>A safe handle for the PipeWire remote file descriptor.</returns>
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(
        ObjectPath sessionHandle,
        IDictionary<string, object> options
    );
}
