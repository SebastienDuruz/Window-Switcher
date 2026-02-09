using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSwitcher.Theming;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Data.WindowAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel(Action applyAction) : ObservableObject
{
    private readonly ConfigFileAccessor _configAccessor = ConfigFileAccessor.GetInstance();
    public bool ShowWindowDecorationsVisible => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool LinuxScreenshotSettingsVisible => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public bool LinuxScreenshotQualityVisible => LinuxScreenshotSettingsVisible;
    public bool LinuxPipeWireSettingsVisible => LinuxScreenshotSettingsVisible;
    public string[] LinuxPreviewBackendOptions => Enum.GetNames<LinuxPreviewBackend>();

    public IRelayCommand ApplyCommand { get; } = new RelayCommand(applyAction);

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

    public int ScreenshotQuality
    {
        get => _configAccessor.ReadConfig(config => config.ScreenshotQuality);
        set
        {
            int clamped = Math.Clamp(value, 1, 100);
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.ScreenshotQuality == clamped)
                    return;
                config.ScreenshotQuality = clamped;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public int ScreenshotRefreshTimeoutMs
    {
        get => _configAccessor.ReadConfig(config => config.ScreenshotRefreshTimeoutMs);
        set
        {
            int clamped = Math.Clamp(value, 100, 10_000);
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.ScreenshotRefreshTimeoutMs == clamped)
                    return;
                config.ScreenshotRefreshTimeoutMs = clamped;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public string LinuxPreviewBackend
    {
        get => _configAccessor.ReadConfig(config => NormalizeLinuxPreviewBackend(config.LinuxPreviewBackend));
        set
        {
            string normalized = NormalizeLinuxPreviewBackend(value);
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (string.Equals(config.LinuxPreviewBackend, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
                config.LinuxPreviewBackend = normalized;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public int LinuxPipeWireFps
    {
        get => _configAccessor.ReadConfig(config => config.LinuxPipeWireFps);
        set
        {
            int clamped = Math.Clamp(value, 1, 60);
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.LinuxPipeWireFps == clamped)
                    return;
                config.LinuxPipeWireFps = clamped;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public int LinuxPipeWireReconnectDelayMs
    {
        get => _configAccessor.ReadConfig(config => config.LinuxPipeWireReconnectDelayMs);
        set
        {
            int clamped = Math.Clamp(value, 100, 30_000);
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (config.LinuxPipeWireReconnectDelayMs == clamped)
                    return;
                config.LinuxPipeWireReconnectDelayMs = clamped;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
        }
    }

    public string LinuxPipeWireNodeId
    {
        get => _configAccessor.ReadConfig(config => config.LinuxPipeWireNodeId ?? string.Empty);
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            bool updated = false;
            _configAccessor.UpdateConfig(config =>
            {
                if (string.Equals(config.LinuxPipeWireNodeId, normalized, StringComparison.Ordinal))
                    return;
                config.LinuxPipeWireNodeId = normalized;
                updated = true;
            });
            if (updated)
                OnPropertyChanged();
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

    private static string NormalizeLinuxPreviewBackend(string? value)
    {
        if (Enum.TryParse(value, ignoreCase: true, out WindowSwitcherLib.Models.LinuxPreviewBackend backend))
            return backend.ToString();

        return WindowSwitcherLib.Models.LinuxPreviewBackend.Auto.ToString();
    }
}
