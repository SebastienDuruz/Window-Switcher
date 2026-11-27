using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the main window: the switchable window list and the menu commands.
/// Underlying UI orchestration is provided through delegates to keep the view
/// model UI-agnostic and unit-testable.
/// </summary>
public sealed class MainWindowViewModel
{
    public WindowListViewModel WindowList { get; }

    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenAboutCommand { get; }
    public IRelayCommand OpenConfigFolderCommand { get; }
    public IRelayCommand OpenLogsFolderCommand { get; }
    public IRelayCommand<string> AddToBlacklistCommand { get; }
    public IRelayCommand<string> AddToTemporaryBlacklistCommand { get; }
    public IAsyncRelayCommand<string> RenameWindowCommand { get; }

    public MainWindowViewModel(
        WindowListViewModel windowList,
        Action openSettings,
        Action openAbout,
        Action openConfigFolder,
        Action openLogsFolder,
        Action<string?> addToBlacklist,
        Action<string?> addToTemporaryBlacklist,
        Func<string?, Task> renameWindow
    )
    {
        ArgumentNullException.ThrowIfNull(windowList);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(openAbout);
        ArgumentNullException.ThrowIfNull(openConfigFolder);
        ArgumentNullException.ThrowIfNull(openLogsFolder);
        ArgumentNullException.ThrowIfNull(addToBlacklist);
        ArgumentNullException.ThrowIfNull(addToTemporaryBlacklist);
        ArgumentNullException.ThrowIfNull(renameWindow);

        WindowList = windowList;

        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenAboutCommand = new RelayCommand(openAbout);
        OpenConfigFolderCommand = new RelayCommand(openConfigFolder);
        OpenLogsFolderCommand = new RelayCommand(openLogsFolder);
        AddToBlacklistCommand = new RelayCommand<string>(addToBlacklist);
        AddToTemporaryBlacklistCommand = new RelayCommand<string>(addToTemporaryBlacklist);
        RenameWindowCommand = new AsyncRelayCommand<string>(renameWindow);
    }
}
