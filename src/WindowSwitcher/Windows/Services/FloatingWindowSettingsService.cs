using System;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Windows.Services;

internal interface IFloatingWindowSettingsService
{
    WindowConfig? GetPersistedConfig(WindowConfig runtimeConfig);

    FloatingWindowBehaviorSettings GetBehaviorSettings();

    FloatingWindowAppearanceSettings GetAppearanceSettings();

    void Save(WindowConfig windowConfig);
}

internal sealed record FloatingWindowBehaviorSettings(bool MoveWindows, bool FocusOnHover);

internal sealed record FloatingWindowAppearanceSettings(
    bool ResizeWindows,
    bool UseFixedWindowSize,
    int WindowWidth,
    int WindowHeight,
    string PreviewHighlightColor
);

internal sealed class ConfigFileFloatingWindowSettingsService : IFloatingWindowSettingsService
{
    public WindowConfig? GetPersistedConfig(WindowConfig runtimeConfig)
    {
        ArgumentNullException.ThrowIfNull(runtimeConfig);

        return ConfigFileAccessor.GetInstance().GetFloatingWindowConfig(runtimeConfig);
    }

    public FloatingWindowBehaviorSettings GetBehaviorSettings()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => new FloatingWindowBehaviorSettings(
                config.MoveWindows,
                config.FocusOnHover
            ));
    }

    public FloatingWindowAppearanceSettings GetAppearanceSettings()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => new FloatingWindowAppearanceSettings(
                config.ResizeWindows,
                config.UseFixedWindowSize,
                config.WindowWidth,
                config.WindowHeight,
                config.PreviewHighlightColor
            ));
    }

    public void Save(WindowConfig windowConfig)
    {
        ArgumentNullException.ThrowIfNull(windowConfig);

        ConfigFileAccessor.GetInstance().SaveFloatingWindowSettings(windowConfig);
    }
}
