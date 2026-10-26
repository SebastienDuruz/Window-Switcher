using Newtonsoft.Json;
using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;

/// <summary>
/// Builds the JavaScript sources loaded into KWin. Every interpolated value is encoded as a JSON
/// string literal, so no caller-provided text can change the script.
/// </summary>
internal static class KWinScripts
{
    internal const string BridgeInterfaceName =
        "io.github.sebastienduruz.WindowSwitcher.KWinBridge";
    internal static readonly ObjectPath BridgePath = new(
        "/io/github/sebastienduruz/WindowSwitcher/KWinBridge"
    );

    // KWin exposes no X11 flag to scripts; the wrapped QObject class name tells the backends apart
    // ("KWin::X11Window" for XWayland, "KWin::XdgToplevelWindow" for native Wayland windows).
    private const string WatcherTemplate = """
        "use strict";
        const busName = __BUS__;
        const bridgePath = __PATH__;
        const bridgeInterface = __INTERFACE__;

        function describe(window) {
            return {
                id: window.internalId.toString(),
                caption: window.captionNormal,
                pid: window.pid,
                normal: window.normalWindow,
                skipTaskbar: window.skipTaskbar,
                x11: String(window).indexOf("X11Window") !== -1
            };
        }

        function push(removedWindow) {
            const windows = workspace.windowList()
                .filter(function (window) { return window !== removedWindow; })
                .map(describe);
            callDBus(busName, bridgePath, bridgeInterface, "PushSnapshot", JSON.stringify(windows));
        }

        function watch(window) {
            window.captionChanged.connect(function () { push(null); });
            if (window.skipTaskbarChanged)
                window.skipTaskbarChanged.connect(function () { push(null); });
        }

        workspace.windowList().forEach(watch);
        workspace.windowAdded.connect(function (window) { watch(window); push(null); });
        workspace.windowRemoved.connect(function (window) { push(window); });
        push(null);
        """;

    private const string ActivationTemplate = """
        "use strict";
        const target = workspace.windowList().find(function (window) {
            return window.internalId.toString() === __WINDOW__;
        });
        if (target) {
            if (target.minimized)
                target.minimized = false;
            workspace.activeWindow = target;
        }
        callDBus(__BUS__, __PATH__, __INTERFACE__, "CompleteActivation", __REQUEST__, target !== undefined);
        """;

    /// <summary>
    /// Builds the long-lived script that pushes the window list on every relevant change.
    /// </summary>
    internal static string BuildWatcher(string busName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(busName);
        return ReplaceBridgeTokens(WatcherTemplate, busName);
    }

    /// <summary>
    /// Builds a one-shot script that activates a window and reports the outcome.
    /// </summary>
    internal static string BuildActivation(string busName, Guid windowId, string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(busName);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return ReplaceBridgeTokens(ActivationTemplate, busName)
            .Replace("__WINDOW__", Literal(FormatWindowId(windowId)), StringComparison.Ordinal)
            .Replace("__REQUEST__", Literal(requestId), StringComparison.Ordinal);
    }

    /// <summary>Formats a window UUID the way KWin's <c>internalId.toString()</c> does.</summary>
    internal static string FormatWindowId(Guid windowId) => windowId.ToString("B");

    private static string ReplaceBridgeTokens(string template, string busName)
    {
        return template
            .Replace("__BUS__", Literal(busName), StringComparison.Ordinal)
            .Replace("__PATH__", Literal(BridgePath.ToString()), StringComparison.Ordinal)
            .Replace("__INTERFACE__", Literal(BridgeInterfaceName), StringComparison.Ordinal);
    }

    private static string Literal(string value) => JsonConvert.ToString(value);
}
