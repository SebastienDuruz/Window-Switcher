namespace WindowSwitcherLib.Models;

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
    
    // Debug
    public bool ActivateWindowsPreview { get; set; } = true;
    public bool ActivateLogs { get; set; } = false;
    
    // Screenshot methods
    public int ScreenshotRefreshTimeoutMs { get; set; } = 1000;
    public int ScreenshotQuality { get; set; } = 5;

    // Linux preview backend
    public string LinuxPreviewBackend { get; set; } = "Auto";
    public int LinuxPipeWireFps { get; set; } = 15;
    public int LinuxPipeWireRefreshTimeoutMs { get; set; } = 100;
    public int LinuxPipeWireReconnectDelayMs { get; set; } = 1000;
    public string LinuxPipeWireNodeId { get; set; } = string.Empty;
    
    // Prefix / Blacklist
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();
    
    // Remember floating windows positions
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();
}
