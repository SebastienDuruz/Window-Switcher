using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly Action _applyAction;
    private readonly Action<string> _applyPreviewHighlightColorAction;

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        Action applyAction,
        Action<string> applyPreviewHighlightColorAction
    )
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(applyAction);
        ArgumentNullException.ThrowIfNull(applyPreviewHighlightColorAction);

        _settingsRepository = settingsRepository;
        _applyAction = applyAction;
        _applyPreviewHighlightColorAction = applyPreviewHighlightColorAction;
    }

    public bool EnablePreviews
    {
        get => ReadSetting(config => config.EnablePreviews);
        set =>
            UpdateSetting(
                nameof(EnablePreviews),
                value,
                config => config.EnablePreviews,
                (config, currentValue) => config.EnablePreviews = currentValue,
                applyToWindows: true
            );
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
                (config, currentValue) => config.ResizeWindows = currentValue,
                applyToWindows: true
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
                (config, currentValue) => config.UseFixedWindowSize = currentValue,
                applyToWindows: true
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
                (config, currentValue) => config.WindowWidth = currentValue,
                applyToWindows: true
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
                (config, currentValue) => config.WindowHeight = currentValue,
                applyToWindows: true
            );
    }

    public string PreviewHighlightColor
    {
        get
        {
            string value = _settingsRepository.Read(config => config.PreviewHighlightColor);
            return string.IsNullOrWhiteSpace(value) ? "#E3008C" : value;
        }
        set
        {
            string configValue = string.IsNullOrWhiteSpace(value) ? "#E3008C" : value.Trim();

            bool updated = false;
            _settingsRepository.Update(config =>
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
                _applyPreviewHighlightColorAction(configValue);
                _applyAction();
                OnPropertyChanged();
            }
        }
    }

    private T ReadSetting<T>(Func<ConfigFile, T> selector)
    {
        return _settingsRepository.Read(selector);
    }

    private bool UpdateSetting<T>(
        string propertyName,
        T newValue,
        Func<ConfigFile, T> selector,
        Action<ConfigFile, T> updater,
        bool applyToWindows = false
    )
    {
        bool updated = false;
        _settingsRepository.Update(config =>
        {
            if (EqualityComparer<T>.Default.Equals(selector(config), newValue))
                return;

            updater(config, newValue);
            updated = true;
        });

        if (updated)
        {
            OnPropertyChanged(propertyName);
            if (applyToWindows)
                _applyAction();
        }

        return updated;
    }
}
