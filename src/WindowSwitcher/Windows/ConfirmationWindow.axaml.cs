using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WindowSwitcher.ViewModels;

namespace WindowSwitcher.Windows;

/// <summary>
/// Modal dialog asking the user to confirm a destructive action.
/// </summary>
public partial class ConfirmationWindow : Window
{
    private readonly ConfirmationViewModel? _viewModel;

    public ConfirmationWindow()
    {
        InitializeComponent();
    }

    private ConfirmationWindow(ConfirmationViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.Completed += OnCompleted;
        Closed += OnClosed;
    }

    /// <summary>
    /// Shows <paramref name="request"/> as a modal dialog over <paramref name="owner"/>.
    /// Returns <see langword="true"/> only when the user confirms; closing the dialog
    /// counts as cancelling.
    /// </summary>
    public static Task<bool> ShowAsync(Window owner, ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var window = new ConfirmationWindow(new ConfirmationViewModel(request));
        return window.ShowDialog<bool>(owner);
    }

    private void OnCompleted(object? sender, bool confirmed)
    {
        Close(confirmed);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.Completed -= OnCompleted;
        Closed -= OnClosed;
    }
}
