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
    public IRelayCommand OpenFiltersCommand { get; }

    /// <summary>
    /// Activates the window whose id is the command parameter, like a click on its preview.
    /// </summary>
    public IAsyncRelayCommand<string> ActivateWindowCommand { get; }
    public IRelayCommand OpenConfigFolderCommand { get; }
    public IRelayCommand OpenLogsFolderCommand { get; }
    public IRelayCommand<string> AddToBlacklistCommand { get; }
    public IRelayCommand<string> AddToTemporaryBlacklistCommand { get; }
    public IAsyncRelayCommand<string> RenameWindowCommand { get; }

    /// <summary>
    /// Moves the preview of the window whose id is the command parameter next to the
    /// main window.
    /// </summary>
    public IRelayCommand<string> BringPreviewHereCommand { get; }

    public MainWindowViewModel(
        WindowListViewModel windowList,
        Action openSettings,
        Action openFilters,
        Action openConfigFolder,
        Action openLogsFolder,
        Action<string?> addToBlacklist,
        Action<string?> addToTemporaryBlacklist,
        Func<string?, Task> renameWindow,
        Action<string?> bringPreviewHere,
        Func<string?, Task> activateWindow
    )
    {
        ArgumentNullException.ThrowIfNull(windowList);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(openFilters);
        ArgumentNullException.ThrowIfNull(openConfigFolder);
        ArgumentNullException.ThrowIfNull(openLogsFolder);
        ArgumentNullException.ThrowIfNull(addToBlacklist);
        ArgumentNullException.ThrowIfNull(addToTemporaryBlacklist);
        ArgumentNullException.ThrowIfNull(renameWindow);
        ArgumentNullException.ThrowIfNull(bringPreviewHere);
        ArgumentNullException.ThrowIfNull(activateWindow);

        WindowList = windowList;

        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenFiltersCommand = new RelayCommand(openFilters);
        OpenConfigFolderCommand = new RelayCommand(openConfigFolder);
        OpenLogsFolderCommand = new RelayCommand(openLogsFolder);
        AddToBlacklistCommand = new RelayCommand<string>(addToBlacklist);
        AddToTemporaryBlacklistCommand = new RelayCommand<string>(addToTemporaryBlacklist);
        RenameWindowCommand = new AsyncRelayCommand<string>(renameWindow);
        BringPreviewHereCommand = new RelayCommand<string>(bringPreviewHere);
        ActivateWindowCommand = new AsyncRelayCommand<string>(activateWindow);
    }
}
