using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowSwitcher.Lib.Models;

public class WindowConfig : INotifyPropertyChanged
{
    private string _windowTitle = string.Empty;
    private string _shortWindowTitle = string.Empty;
    private double _windowWidth = 100;
    private double _windowHeight = 83;
    private int _windowLeft = 100;
    private int _windowTop = 100;

    public string WindowId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;

    public string WindowTitle
    {
        get => _windowTitle;
        set
        {
            if (_windowTitle == value)
                return;
            _windowTitle = value;
            ShortWindowTitle = _windowTitle.Length > 40 ? $"{_windowTitle[..40]}..." : _windowTitle;
            OnPropertyChanged();
        }
    }

    public string ShortWindowTitle
    {
        get => _shortWindowTitle;
        set
        {
            if (_shortWindowTitle == value)
                return;
            _shortWindowTitle = value;
            OnPropertyChanged();
        }
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            if (_windowWidth == value)
                return;
            _windowWidth = value;
            OnPropertyChanged();
        }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set
        {
            if (_windowHeight == value)
                return;
            _windowHeight = value;
            OnPropertyChanged();
        }
    }

    public int WindowLeft
    {
        get => _windowLeft;
        set
        {
            if (_windowLeft == value)
                return;
            _windowLeft = value;
            OnPropertyChanged();
        }
    }

    public int WindowTop
    {
        get => _windowTop;
        set
        {
            if (_windowTop == value)
                return;
            _windowTop = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public WindowConfig Clone()
    {
        return (WindowConfig)this.MemberwiseClone();
    }
}
