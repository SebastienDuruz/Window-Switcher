using System;
using System.Linq;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class ConfigFileWindowFilterSettingsProvider : IWindowFilterSettingsProvider
{
    public WindowFilterSettings GetSettings()
    {
        return ConfigFileAccessor
            .GetInstance()
            .ReadConfig(config => new WindowFilterSettings(
                config.BlacklistPrefixes.ToHashSet(StringComparer.OrdinalIgnoreCase),
                config
                    .WhitelistPrefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                    .ToArray()
            ));
    }
}
