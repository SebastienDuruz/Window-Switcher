using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.Theming;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ConfigFileAccessor _configAccessor = ConfigFileAccessor.GetInstance();
    private readonly Action _applyAction;
    private bool _pendingEnablePreviews;
    public IRelayCommand ApplyCommand { get; }

    public SettingsViewModel(Action applyAction)
    {
        ArgumentNullException.ThrowIfNull(applyAction);

        _applyAction = applyAction;
        _pendingEnablePreviews = _configAccessor.ReadConfig(config => config.EnablePreviews);
        ApplyCommand = new RelayCommand(Apply);
    }

    public bool EnablePreviews
    {
        get => _pendingEnablePreviews;
        set
        {
            if (_pendingEnablePreviews == value)
                return;
            _pendingEnablePreviews = value;
            OnPropertyChanged();
        }
    }

    public void ResetPendingValues()
    {
        bool configuredValue = _configAccessor.ReadConfig(config => config.EnablePreviews);
        if (_pendingEnablePreviews == configuredValue)
            return;

        _pendingEnablePreviews = configuredValue;
        OnPropertyChanged(nameof(EnablePreviews));
    }

    public bool StartMinimized
    {
        get => ReadSetting(config => config.StartMinimized);
        set =>
            UpdateSetting(
                nameof(StartMinimized),
                value,
                config => config.StartMinimized,
                (config, currentValue) => config.StartMinimized = currentValue
            );
    }

    public bool ResizeWindows
    {
        get => ReadSetting(config => config.ResizeWindows);
        set =>
            UpdateSetting(
                nameof(ResizeWindows),
                value,
                config => config.ResizeWindows,
                (config, currentValue) => config.ResizeWindows = currentValue
            );
    }

    public bool CanEditResizeWindows => !UseFixedWindowSize;

    public bool MoveWindows
    {
        get => ReadSetting(config => config.MoveWindows);
        set =>
            UpdateSetting(
                nameof(MoveWindows),
                value,
                config => config.MoveWindows,
                (config, currentValue) => config.MoveWindows = currentValue
            );
    }

    public bool FocusOnHover
    {
        get => ReadSetting(config => config.FocusOnHover);
        set =>
            UpdateSetting(
                nameof(FocusOnHover),
                value,
                config => config.FocusOnHover,
                (config, currentValue) => config.FocusOnHover = currentValue
            );
    }

    public bool UseFixedWindowSize
    {
        get => ReadSetting(config => config.UseFixedWindowSize);
        set
        {
            bool updated = UpdateSetting(
                nameof(UseFixedWindowSize),
                value,
                config => config.UseFixedWindowSize,
                (config, currentValue) => config.UseFixedWindowSize = currentValue
            );
            if (updated)
                OnPropertyChanged(nameof(CanEditResizeWindows));
        }
    }

    public int WindowWidth
    {
        get => ReadSetting(config => config.WindowWidth);
        set =>
            UpdateSetting(
                nameof(WindowWidth),
                value,
                config => config.WindowWidth,
                (config, currentValue) => config.WindowWidth = currentValue
            );
    }

    public int WindowHeight
    {
        get => ReadSetting(config => config.WindowHeight);
        set =>
            UpdateSetting(
                nameof(WindowHeight),
                value,
                config => config.WindowHeight,
                (config, currentValue) => config.WindowHeight = currentValue
            );
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
                if (
                    string.Equals(
                        config.PreviewHighlightColor,
                        configValue,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
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

    private void Apply()
    {
        _configAccessor.UpdateConfig(config => config.EnablePreviews = _pendingEnablePreviews);
        _applyAction();
    }

    private T ReadSetting<T>(Func<ConfigFile, T> selector)
    {
        return _configAccessor.ReadConfig(selector);
    }

    private bool UpdateSetting<T>(
        string propertyName,
        T newValue,
        Func<ConfigFile, T> selector,
        Action<ConfigFile, T> updater
    )
    {
        bool updated = false;
        _configAccessor.UpdateConfig(config =>
        {
            if (EqualityComparer<T>.Default.Equals(selector(config), newValue))
                return;

            updater(config, newValue);
            updated = true;
        });

        if (updated)
            OnPropertyChanged(propertyName);

        return updated;
    }
}
