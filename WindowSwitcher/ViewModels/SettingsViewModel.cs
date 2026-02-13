using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcherLib.Application.Platform;
using WindowSwitcher.Theming;
using WindowSwitcherLib.Data.Configuration;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ConfigFileAccessor _configAccessor = ConfigFileAccessor.GetInstance();
    private readonly ISettingsPlatformPolicy _settingsPlatformPolicy;
    public IRelayCommand ApplyCommand { get; }

    public SettingsViewModel(Action applyAction, ISettingsPlatformPolicy settingsPlatformPolicy)
    {
        ArgumentNullException.ThrowIfNull(applyAction);
        ArgumentNullException.ThrowIfNull(settingsPlatformPolicy);

        _settingsPlatformPolicy = settingsPlatformPolicy;
        ApplyCommand = new RelayCommand(applyAction);
    }

    public bool ShowWindowDecorationSetting => _settingsPlatformPolicy.ShowWindowDecorationSetting;

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

    public bool FocusOnHover
    {
        get => _configAccessor.ReadConfig(config => config.FocusOnHover);
        set
        {
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.FocusOnHover == value)
                    return;
                config.FocusOnHover = value;
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

    public Color PreviewHighlightColor
    {
        get
        {
            string value = _configAccessor.ReadConfig(config => config.PreviewHighlightColor);
            if (Color.TryParse(value, out Color color))
                return color;

            // Defensive fallback for corrupted config values.
            return Colors.Magenta;
        }
        set
        {
            string configValue = ToConfigColorString(value);

            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (string.Equals(config.PreviewHighlightColor, configValue, StringComparison.OrdinalIgnoreCase))
                    return;
                config.PreviewHighlightColor = configValue;
                updated = true;
            });
            if (updated)
            {
                AccentColorApplier.Apply(value);
                OnPropertyChanged();
            }
        }
    }

    private static string ToConfigColorString(Color color)
    {
        // Keep a stable, human-friendly format in the config file.
        // - #RRGGBB when fully opaque
        // - #AARRGGBB when transparent
        return color.A == 0xFF
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
