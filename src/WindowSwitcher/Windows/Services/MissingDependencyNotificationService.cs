using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Windows.Services;

internal sealed class MissingDependencyNotificationService : IDisposable
{
    private readonly Window _ownerWindow;
    private readonly HashSet<string> _missingDependencies = new(StringComparer.OrdinalIgnoreCase);
    private Window? _dialog;
    private TextBlock? _textBlock;

    public MissingDependencyNotificationService(Window ownerWindow)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);

        _ownerWindow = ownerWindow;
    }

    public void RegisterMissingDependencies(IEnumerable<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        bool hasChanges = false;
        foreach (string dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency))
                continue;

            hasChanges |= _missingDependencies.Add(dependency);
        }

        if (hasChanges)
            ShowOrUpdateDialog();
    }

    public void RegisterMissingDependency(string dependency)
    {
        if (StaticData.AppClosing)
            return;
        if (string.IsNullOrWhiteSpace(dependency))
            return;
        if (!_missingDependencies.Add(dependency))
            return;

        ShowOrUpdateDialog();
    }

    public void Dispose()
    {
        if (_dialog is not null)
            _dialog.Closed -= OnDialogClosed;

        _dialog?.Close();
        _dialog = null;
        _textBlock = null;
    }

    private void ShowOrUpdateDialog()
    {
        if (_missingDependencies.Count == 0)
            return;

        if (_dialog is null || _textBlock is null)
            _dialog = CreateDialog();

        Window dialog =
            _dialog
            ?? throw new InvalidOperationException("The dependency dialog was not created.");
        TextBlock textBlock =
            _textBlock
            ?? throw new InvalidOperationException(
                "The dependency dialog content was not created."
            );

        dialog.Title = DependencyNotificationDialogContent.CreateTitle(_missingDependencies.Count);
        textBlock.Text = DependencyNotificationDialogContent.CreateMessage(_missingDependencies);

        if (dialog.IsVisible)
            return;

        if (_ownerWindow.IsVisible)
            _ = dialog.ShowDialog(_ownerWindow);
        else
            dialog.Show();
    }

    private Window CreateDialog()
    {
        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        _textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_textBlock);
        panel.Children.Add(okButton);

        var dialog = new Window
        {
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel,
        };

        okButton.Click += (_, _) => dialog.Close();
        dialog.Closed += OnDialogClosed;
        return dialog;
    }

    private void OnDialogClosed(object? sender, EventArgs e)
    {
        Window? dialog = _dialog;
        if (dialog is null || !ReferenceEquals(sender, dialog))
            return;

        dialog.Closed -= OnDialogClosed;
        _dialog = null;
        _textBlock = null;
    }
}
