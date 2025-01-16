namespace WindowSwitcherLib.Models;

public class WindowConfig
{
    public string WindowId { get; set; }
    public string WindowTitle { get; set; }
    public string ShortWindowTitle { get; set; }
    public double WindowWidth { get; set; } = 100;
    public double WindowHeight { get; set; } = 83;
    public int WindowLeft { get; set; } = 100;
    public int WindowTop { get; set; } = 100;
}