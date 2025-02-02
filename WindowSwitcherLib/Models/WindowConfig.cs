namespace WindowSwitcherLib.Models;

public class WindowConfig
{
    private string _windowTitle;
    public string WindowId { get; set; }

    public string WindowTitle
    {
        get => _windowTitle;
        set
        {
            if (_windowTitle != value)
            {
                _windowTitle = value;
                ShortWindowTitle = _windowTitle.Length > 40 ? $"{_windowTitle[..40]}..." : _windowTitle;
            }
        }
    }

    public string ShortWindowTitle { get; set; }
    public double WindowWidth { get; set; } = 100;
    public double WindowHeight { get; set; } = 83;
    public int WindowLeft { get; set; } = 100;
    public int WindowTop { get; set; } = 100;

    public WindowConfig Clone()
    {
        return (WindowConfig)this.MemberwiseClone();
    }
}