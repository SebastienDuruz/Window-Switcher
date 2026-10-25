using Newtonsoft.Json;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin;

/// <summary>
/// Describes one window reported by the KWin watcher script.
/// </summary>
internal sealed record KWinWindow(
    Guid Id,
    string Caption,
    int ProcessId,
    bool IsNormalWindow,
    bool SkipTaskbar,
    bool IsX11
)
{
    /// <summary>
    /// Gets whether the window is a native Wayland application window that belongs in the list.
    /// XWayland windows are already listed through EWMH.
    /// </summary>
    public bool IsListedNativeWindow => IsNormalWindow && !SkipTaskbar && !IsX11;

    /// <summary>
    /// Parses the JSON array pushed by the watcher script.
    /// </summary>
    /// <param name="snapshotJson">JSON array produced by the script.</param>
    /// <param name="windows">The parsed windows; entries without a valid identifier are skipped.</param>
    /// <returns><see langword="false" /> when the payload is not a JSON array of windows.</returns>
    public static bool TryParseSnapshot(string? snapshotJson, out IReadOnlyList<KWinWindow> windows)
    {
        windows = [];
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return false;

        KWinWindowPayload?[]? payloads;
        try
        {
            payloads = JsonConvert.DeserializeObject<KWinWindowPayload?[]>(snapshotJson);
        }
        catch (JsonException)
        {
            // The caller reports the invalid payload; its content may contain window titles.
            return false;
        }

        if (payloads is null)
            return false;

        windows = payloads.Select(payload => payload?.ToWindow()).OfType<KWinWindow>().ToArray();
        return true;
    }

    private sealed class KWinWindowPayload
    {
        public string? Id { get; set; }
        public string? Caption { get; set; }
        public int Pid { get; set; }
        public bool Normal { get; set; }
        public bool SkipTaskbar { get; set; }
        public bool X11 { get; set; }

        public KWinWindow? ToWindow()
        {
            if (!Guid.TryParse(Id, out Guid id) || id == Guid.Empty)
                return null;

            return new KWinWindow(id, Caption ?? string.Empty, Pid, Normal, SkipTaskbar, X11);
        }
    }
}
