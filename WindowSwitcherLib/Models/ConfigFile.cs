namespace WindowSwitcherLib.Models;

public class ConfigFile
{
    public List<string> WhitelistPrefixes { get; set; } = new List<string>();
    public List<string> BlacklistPrefixes { get; set; } = new List<string>();
    public bool StartMinimized { get; set; } = false;
    public List<WindowConfig> FloatingWindowsConfig { get; set; } = new List<WindowConfig>();
}