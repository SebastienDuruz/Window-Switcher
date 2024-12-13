namespace WindowSwitcherLib.Models;

public class ConfigFile
{
    public List<WindowConfig> Windows { get; set; } = new List<WindowConfig>();

    public bool StartMinimized { get; set; } = false;
}