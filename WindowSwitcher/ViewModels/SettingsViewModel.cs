using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ConfigFileAccessor _configAccessor = ConfigFileAccessor.GetInstance();

    public SettingsViewModel(Action applyAction)
    {
        ApplyCommand = new RelayCommand(applyAction);
    }

    public IRelayCommand ApplyCommand { get; }

    public bool StartMinimized
    {
        get => _configAccessor.ReadConfig(config => config.StartMinimized);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.StartMinimized == value)
                    return;
                config.StartMinimized = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public bool ResizeWindows
    {
        get => _configAccessor.ReadConfig(config => config.ResizeWindows);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.ResizeWindows == value)
                    return;
                config.ResizeWindows = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public bool MoveWindows
    {
        get => _configAccessor.ReadConfig(config => config.MoveWindows);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.MoveWindows == value)
                    return;
                config.MoveWindows = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public bool ShowWindowDecorations
    {
        get => _configAccessor.ReadConfig(config => config.ShowWindowDecorations);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.ShowWindowDecorations == value)
                    return;
                config.ShowWindowDecorations = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public bool UseFixedWindowSize
    {
        get => _configAccessor.ReadConfig(config => config.UseFixedWindowSize);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.UseFixedWindowSize == value)
                    return;
                config.UseFixedWindowSize = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public int WindowWidth
    {
        get => _configAccessor.ReadConfig(config => config.WindowWidth);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.WindowWidth == value)
                    return;
                config.WindowWidth = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public int WindowHeight
    {
        get => _configAccessor.ReadConfig(config => config.WindowHeight);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.WindowHeight == value)
                    return;
                config.WindowHeight = value;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }
}
