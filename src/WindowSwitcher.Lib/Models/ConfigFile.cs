using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

namespace WindowSwitcher.Lib.Models;

public class ConfigFile
{
    // General settings
    public bool ResizeWindows { get; set; } = true;
    public bool MoveWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool EnableSentry { get; set; } = true;
    public string SentryDsn { get; set; } = "https://d4530a120abfe1da5f25305d84502c0a@o4511093969584128.ingest.de.sentry.io/4511093982036048";
    public string TelemetryUserId { get; set; } = Guid.NewGuid().ToString("D");
    public bool ShowWindowDecorations { get; set; } = false;
    public bool UseFixedWindowSize { get; set; } = false;
    public bool FocusOnHover { get; set; } = false;
    public int WindowWidth { get; set; } = 300;
    public int WindowHeight { get; set; } = 200;
    public string PreviewHighlightColor { get; set; } = "#E3008C";

    public bool DisablePreviews { get; set; } = false;
    public bool ActivateWindowsPreview { get; set; } = true;

    // Prefix / Blacklist
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();

    // Remember floating windows positions
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();

    // Global keybinds by stable window target
    public List<WindowKeybindTargetConfig> WindowKeybindTargets { get; set; } = [];
}
