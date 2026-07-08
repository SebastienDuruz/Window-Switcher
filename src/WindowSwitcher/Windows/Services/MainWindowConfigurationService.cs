using System.Collections.Generic;
using System.Linq;
using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Windows.Services;

internal interface IMainWindowConfigurationService
{
    IReadOnlyList<string> GetWhitelistPrefixes();

    IReadOnlyList<string> GetBlacklistPrefixes();

    bool ShouldStartMinimized();

    void ResetFloatingWindowSettings();

    void ResetUserSettings();

    void PersistUserSettings();
}

internal sealed class ConfigFileMainWindowConfigurationService : IMainWindowConfigurationService
{
    public IReadOnlyList<string> GetWhitelistPrefixes()
    {
        return ConfigFileAccessor.GetInstance().ReadConfig(config =>
            config.WhitelistPrefixes.ToList()
        );
    }

    public IReadOnlyList<string> GetBlacklistPrefixes()
    {
        return ConfigFileAccessor.GetInstance().ReadConfig(config =>
            config.BlacklistPrefixes.ToList()
        );
    }

    public bool ShouldStartMinimized()
    {
        return ConfigFileAccessor.GetInstance().ReadConfig(config => config.StartMinimized);
    }

    public void ResetFloatingWindowSettings()
    {
        ConfigFileAccessor.GetInstance().ResetFloatingWindowSettings();
    }

    public void ResetUserSettings()
    {
        ConfigFileAccessor.GetInstance().ResetUserSettings();
    }

    public void PersistUserSettings()
    {
        ConfigFileAccessor.GetInstance().WriteUserSettings();
    }
}
