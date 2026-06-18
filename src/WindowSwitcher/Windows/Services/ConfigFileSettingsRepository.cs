using System;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels.Abstractions;

namespace WindowSwitcher.Windows.Services;

internal sealed class ConfigFileSettingsRepository : ISettingsRepository
{
    public T Read<T>(Func<ConfigFile, T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return ConfigFileAccessor.GetInstance().ReadConfig(reader);
    }

    public void Update(Action<ConfigFile> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        ConfigFileAccessor.GetInstance().UpdateConfig(update);
    }
}
