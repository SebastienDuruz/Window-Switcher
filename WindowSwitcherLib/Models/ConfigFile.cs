namespace WindowSwitcherLib.Models;

public class ConfigFile
{
    // General settings
    public bool ResizeWindows { get; set; } = true;
    public bool MoveWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool ShowWindowDecorations { get; set; } = false;
    
    // Debug
    public bool ActivateWindowsPreview { get; set; } = true;
    public bool ActivateLogs { get; set; } = false;
    
    // Screenshot methods
    public int RefreshTimeoutMs { get; set; } = 1000;
    public int ScreenshotQuality { get; set; } = 5;
    
    // Prefix / Blacklist
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();
    
    // Remember floating windows positions
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();
}