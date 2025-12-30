using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ConfigFile _configFile = ConfigFileAccessor.GetInstance().Config;

    public SettingsViewModel(Action applyAction)
    {
        ApplyCommand = new RelayCommand(applyAction);
    }

    public IRelayCommand ApplyCommand { get; }

    public bool StartMinimized
    {
        get => _configFile.StartMinimized;
        set
        {
            if (_configFile.StartMinimized == value)
                return;
            _configFile.StartMinimized = value;
            OnPropertyChanged();
        }
    }

    public bool ResizeWindows
    {
        get => _configFile.ResizeWindows;
        set
        {
            if (_configFile.ResizeWindows == value)
                return;
            _configFile.ResizeWindows = value;
            OnPropertyChanged();
        }
    }

    public bool MoveWindows
    {
        get => _configFile.MoveWindows;
        set
        {
            if (_configFile.MoveWindows == value)
                return;
            _configFile.MoveWindows = value;
            OnPropertyChanged();
        }
    }

    public bool ShowWindowDecorations
    {
        get => _configFile.ShowWindowDecorations;
        set
        {
            if (_configFile.ShowWindowDecorations == value)
                return;
            _configFile.ShowWindowDecorations = value;
            OnPropertyChanged();
        }
    }

    public bool UseFixedWindowSize
    {
        get => _configFile.UseFixedWindowSize;
        set
        {
            if (_configFile.UseFixedWindowSize == value)
                return;
            _configFile.UseFixedWindowSize = value;
            OnPropertyChanged();
        }
    }

    public int WindowWidth
    {
        get => _configFile.WindowWidth;
        set
        {
            if (_configFile.WindowWidth == value)
                return;
            _configFile.WindowWidth = value;
            OnPropertyChanged();
        }
    }

    public int WindowHeight
    {
        get => _configFile.WindowHeight;
        set
        {
            if (_configFile.WindowHeight == value)
                return;
            _configFile.WindowHeight = value;
            OnPropertyChanged();
        }
    }
}
