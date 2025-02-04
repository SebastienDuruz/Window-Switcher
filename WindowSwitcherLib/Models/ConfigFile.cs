namespace WindowSwitcherLib.Models;

public class ConfigFile
{
    public int RefreshTimeoutMs { get; set; } = 2000;
    public int ScreenshotQuality { get; set; } = 5;
    public bool StartMinimized { get; set; } = false;
    public bool ShowWindowDecorations { get; set; } = false;
    public bool ActivateWindowsPreview { get; set; } = true;
    public bool ActivateLogs { get; set; } = false;
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();
    public List<WindowConfig?> FloatingWindowsConfig { get; set; } = new List<WindowConfig?>();
}