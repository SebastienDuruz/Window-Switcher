namespace WindowSwitcherLib.Models;

public class WindowConfig
{
    public string WindowId { get; set; }
    public string WindowTitle { get; set; }
    public string ShortWindowTitle { get; set; }
    public int WindowWidth { get; set; } = 60;
    public int WindowHeight { get; set; } = 40;
    public int WindowLeft { get; set; } = 100;
    public int WindowTop { get; set; } = 100;
    public bool IsSelected { get; set; } = false;
}