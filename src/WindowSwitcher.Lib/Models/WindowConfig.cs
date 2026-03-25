using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowSwitcher.Lib.Models;

public class WindowConfig : INotifyPropertyChanged
{
    public string WindowId { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;

    public string WindowTitle
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            ShortWindowTitle = field.Length > 40 ? $"{field[..40]}..." : field;
            OnPropertyChanged();
        }
    } = string.Empty;

    public string ShortWindowTitle
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    public int WindowWidth
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

    public int WindowHeight
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
        }
    } = 83;

    public int WindowLeft
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

    public int WindowTop
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            OnPropertyChanged();
        }
    } = 100;

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
