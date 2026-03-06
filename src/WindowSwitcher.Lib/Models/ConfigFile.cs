using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Models;

public class ConfigFile
{
    // General settings
    public bool ResizeWindows { get; set; } = true;
    public bool MoveWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool ShowWindowDecorations { get; set; } = false;
    public bool UseFixedWindowSize { get; set; } = false;
    public bool FocusOnHover { get; set; } = false;
    public int WindowWidth { get; set; } = 300;
    public int WindowHeight { get; set; } = 200;
    public string PreviewHighlightColor { get; set; } = "#E3008C";

    public bool DisablePreviews { get; set; } = false;
    public bool ActivateWindowsPreview { get; set; } = true;
    public string LinuxWaylandScreenCastRestoreToken { get; set; } = string.Empty;
    public Dictionary<string, string> LinuxWaylandScreenCastRestoreTokensByWindowId { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, string> LinuxWaylandScreenCastRestoreDataByWindowId { get; set; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, string> LinuxWaylandScreenCastStreamIdsByWindowId { get; set; } =
        new(StringComparer.Ordinal);

    // Prefix / Blacklist
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();

    // Remember floating windows positions
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();

    // Global keybinds by stable window target
    public List<WindowKeybindTargetConfig> WindowKeybindTargets { get; set; } = [];
}
