using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace WindowSwitcher.Theming;

/// <summary>
/// Publishes the "Highlight color" as the <c>HighlightBrush</c> application resource,
/// used by the main window for the active row. The app accent color of the
/// Fluent theme is left untouched, so controls keep a readable accent whatever the
/// highlight color is.
/// </summary>
internal static class HighlightColorApplier
{
    public const string HighlightBrushKey = "HighlightBrush";

    public static void Apply(Color highlightColor)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(highlightColor));
            return;
        }

        if (Application.Current is null)
            return;

        Color opaqueColor = Color.FromArgb(
            0xFF,
            highlightColor.R,
            highlightColor.G,
            highlightColor.B
        );
        Application.Current.Resources[HighlightBrushKey] = new SolidColorBrush(opaqueColor);
    }
}
