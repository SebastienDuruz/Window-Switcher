using System;
using System.ComponentModel;
using Avalonia.Controls;
using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Windows.Services;

internal sealed class UtilityWindowService
{
    public bool HandleClosing(
        Window window,
        CancelEventArgs e,
        bool hideWhenCanceled = true,
        bool hideWhenAllowed = false
    )
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(e);

        e.Cancel = !StaticData.AppClosing;
        if ((e.Cancel && hideWhenCanceled) || (!e.Cancel && hideWhenAllowed))
            window.Hide();

        return e.Cancel;
    }
}
