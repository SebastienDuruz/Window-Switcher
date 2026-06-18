using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly Action _applyAction;
    private readonly Action<string> _applyPreviewHighlightColorAction;
    private bool _pendingEnablePreviews;
    public IRelayCommand ApplyCommand { get; }

    public SettingsViewModel(
        ISettingsRepository settingsRepository,
        Action applyAction,
        Action<string> applyPreviewHighlightColorAction)
    {
        ArgumentNullException.ThrowIfNull(settingsRepository);
        ArgumentNullException.ThrowIfNull(applyAction);
        ArgumentNullException.ThrowIfNull(applyPreviewHighlightColorAction);

        _settingsRepository = settingsRepository;
        _applyAction = applyAction;
        _applyPreviewHighlightColorAction = applyPreviewHighlightColorAction;
        _pendingEnablePreviews = _settingsRepository.Read(config => config.EnablePreviews);
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
        bool configuredValue = _settingsRepository.Read(config => config.EnablePreviews);
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
                OnPropertyChanged();
            }
        }
    }

    private void Apply()
    {
        _settingsRepository.Update(config => config.EnablePreviews = _pendingEnablePreviews);
        _applyAction();
    }

    private T ReadSetting<T>(Func<ConfigFile, T> selector)
    {
        return _settingsRepository.Read(selector);
    }

    private bool UpdateSetting<T>(
        string propertyName,
        T newValue,
        Func<ConfigFile, T> selector,
        Action<ConfigFile, T> updater
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
            OnPropertyChanged(propertyName);

        return updated;
    }
}
