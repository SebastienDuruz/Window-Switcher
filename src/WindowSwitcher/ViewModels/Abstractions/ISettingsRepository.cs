using System;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.ViewModels.Abstractions;

public interface ISettingsRepository
{
    T Read<T>(Func<ConfigFile, T> reader);

    void Update(Action<ConfigFile> update);
}
