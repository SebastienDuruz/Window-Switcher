using Avalonia;
using Avalonia.Controls;

namespace WindowSwitcher.Windows.Settings;

public partial class PrefixListEditor : UserControl
{
    public static readonly StyledProperty<string> PlaceholderProperty = AvaloniaProperty.Register<
        PrefixListEditor,
        string
    >(nameof(Placeholder), string.Empty);

    public PrefixListEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the hint shown in the empty input box.
    /// </summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }
}
