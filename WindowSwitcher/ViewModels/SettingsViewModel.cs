using CommunityToolkit.Mvvm.ComponentModel;
using WindowSwitcherLib.Data.FileAccess;
using WindowSwitcherLib.Models;

namespace WindowSwitcher.ViewModels;

public class SettingsViewModel
{
    public ConfigFile ConfigFile { get; } = ConfigFileAccessor.GetInstance().Config;
}