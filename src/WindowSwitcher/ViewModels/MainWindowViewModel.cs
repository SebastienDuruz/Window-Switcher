using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the main window: the switchable window list and the menu commands.
/// Underlying UI orchestration is provided through delegates to keep the view
/// model UI-agnostic and unit-testable.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    public WindowListViewModel WindowList { get; }

    [ObservableProperty]
    private bool _canResetAllPreviews;

    public IRelayCommand OpenFiltersCommand { get; }
    public IRelayCommand OpenKeybindsCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenAboutCommand { get; }
    public IRelayCommand OpenDataFolderCommand { get; }
    public IRelayCommand ClearConfigCommand { get; }
    public IRelayCommand ResetConfigCommand { get; }
    public IRelayCommand ResetAllPreviewsCommand { get; }
    public IRelayCommand<string> AddToBlacklistCommand { get; }
    public IRelayCommand<string> AddToTemporaryBlacklistCommand { get; }
    public IAsyncRelayCommand<string> RenameWindowCommand { get; }

    public MainWindowViewModel(
        WindowListViewModel windowList,
        Action openFilters,
        Action openKeybinds,
        Action openSettings,
        Action openAbout,
        Action openDataFolder,
        Action clearConfig,
        Action resetConfig,
        Func<bool> canResetAllPreviews,
        Action resetAllPreviews,
        Action<string?> addToBlacklist,
        Action<string?> addToTemporaryBlacklist,
        Func<string?, Task> renameWindow
    )
    {
        ArgumentNullException.ThrowIfNull(windowList);
        ArgumentNullException.ThrowIfNull(openFilters);
        ArgumentNullException.ThrowIfNull(openKeybinds);
        ArgumentNullException.ThrowIfNull(openSettings);
        ArgumentNullException.ThrowIfNull(openAbout);
        ArgumentNullException.ThrowIfNull(openDataFolder);
        ArgumentNullException.ThrowIfNull(clearConfig);
        ArgumentNullException.ThrowIfNull(resetConfig);
        ArgumentNullException.ThrowIfNull(canResetAllPreviews);
        ArgumentNullException.ThrowIfNull(resetAllPreviews);
        ArgumentNullException.ThrowIfNull(addToBlacklist);
        ArgumentNullException.ThrowIfNull(addToTemporaryBlacklist);
        ArgumentNullException.ThrowIfNull(renameWindow);

        WindowList = windowList;

        OpenFiltersCommand = new RelayCommand(openFilters);
        OpenKeybindsCommand = new RelayCommand(openKeybinds);
        OpenSettingsCommand = new RelayCommand(openSettings);
        OpenAboutCommand = new RelayCommand(openAbout);
        OpenDataFolderCommand = new RelayCommand(openDataFolder);
        ClearConfigCommand = new RelayCommand(clearConfig);
        ResetConfigCommand = new RelayCommand(resetConfig);
        ResetAllPreviewsCommand = new RelayCommand(resetAllPreviews, canResetAllPreviews);
        AddToBlacklistCommand = new RelayCommand<string>(addToBlacklist);
        AddToTemporaryBlacklistCommand = new RelayCommand<string>(addToTemporaryBlacklist);
        RenameWindowCommand = new AsyncRelayCommand<string>(renameWindow);
    }

    public void SetPreviewResetAvailability(bool available)
    {
        CanResetAllPreviews = available;
    }

    partial void OnCanResetAllPreviewsChanged(bool value)
    {
        ResetAllPreviewsCommand.NotifyCanExecuteChanged();
    }
}