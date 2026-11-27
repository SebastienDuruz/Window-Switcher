using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowSwitcher.ViewModels;

/// <summary>
/// Backs the settings window: page navigation, the settings shown on the General and
/// Previews pages, and the reset actions. The Filters, Keybinds and About pages bind to
/// their own view models.
/// </summary>
public partial class SettingsWindowViewModel : ObservableObject
{
    internal static readonly ConfirmationRequest ResetLayoutConfirmation = new(
        "Reset preview layout",
        "Forget the saved position and size of every preview?",
        "Reset layout"
    );

    internal static readonly ConfirmationRequest ResetSettingsConfirmation = new(
        "Reset all settings",
        "Restore the default configuration? Filters, keybinds and the preview layout are reset too.",
        "Reset settings"
    );

    private readonly Action _resetLayout;
    private readonly Action _resetSettings;
    private readonly Action? _resetAllPreviews;
    private readonly Func<ConfirmationRequest, Task<bool>> _confirm;
    private readonly Action<SettingsPage> _pageShown;

    [ObservableProperty]
    private int _selectedPageIndex;

    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Gets whether remembered capture selections can be reset (Wayland capture).
    /// </summary>
    public bool CanResetAllPreviews => _resetAllPreviews is not null;

    public SettingsPage SelectedPage => (SettingsPage)SelectedPageIndex;

    public IAsyncRelayCommand ResetLayoutCommand { get; }
    public IAsyncRelayCommand ResetSettingsCommand { get; }
    public IRelayCommand ResetAllPreviewsCommand { get; }

    /// <param name="settings">Settings shown on the General and Previews pages.</param>
    /// <param name="resetLayout">Forgets the saved position and size of every preview.</param>
    /// <param name="resetSettings">Restores the default configuration.</param>
    /// <param name="resetAllPreviews">Resets remembered capture selections, or
    /// <see langword="null"/> when the capture backend has none.</param>
    /// <param name="confirm">Asks the user to confirm a destructive action.</param>
    /// <param name="pageShown">Called each time a page becomes visible.</param>
    public SettingsWindowViewModel(
        SettingsViewModel settings,
        Action resetLayout,
        Action resetSettings,
        Action? resetAllPreviews,
        Func<ConfirmationRequest, Task<bool>> confirm,
        Action<SettingsPage> pageShown
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(resetLayout);
        ArgumentNullException.ThrowIfNull(resetSettings);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(pageShown);

        Settings = settings;
        _resetLayout = resetLayout;
        _resetSettings = resetSettings;
        _resetAllPreviews = resetAllPreviews;
        _confirm = confirm;
        _pageShown = pageShown;

        ResetLayoutCommand = new AsyncRelayCommand(ResetLayoutAsync);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
        ResetAllPreviewsCommand = new RelayCommand(
            () => _resetAllPreviews?.Invoke(),
            () => CanResetAllPreviews
        );
    }

    /// <summary>
    /// Selects <paramref name="page"/>; the page is reported as shown even when it was
    /// already selected, so its content can be refreshed when the window opens.
    /// </summary>
    public void ShowPage(SettingsPage page)
    {
        if (!Enum.IsDefined(page))
            throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown settings page.");

        int index = (int)page;
        if (SelectedPageIndex == index)
        {
            _pageShown(page);
            return;
        }

        SelectedPageIndex = index;
    }

    partial void OnSelectedPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedPage));
        if (Enum.IsDefined((SettingsPage)value))
            _pageShown((SettingsPage)value);
    }

    private async Task ResetLayoutAsync()
    {
        if (!await _confirm(ResetLayoutConfirmation))
            return;

        _resetLayout();
    }

    private async Task ResetSettingsAsync()
    {
        if (!await _confirm(ResetSettingsConfirmation))
            return;

        _resetSettings();
        Settings.Reload();
    }
}
