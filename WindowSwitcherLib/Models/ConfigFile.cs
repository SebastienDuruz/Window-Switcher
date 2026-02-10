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
    
    // Linux preview backend
    public int LinuxPipeWireRefreshTimeoutMs { get; set; } = 100;
    
    // Prefix / Blacklist
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();
    
    // Remember floating windows positions
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();
}
