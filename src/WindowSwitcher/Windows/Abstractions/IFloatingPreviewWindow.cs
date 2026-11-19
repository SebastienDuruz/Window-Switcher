namespace WindowSwitcher.Windows.Abstractions;

public interface IFloatingPreviewWindow
{
    void SetPreviewHighlight(bool isSelected);
    void SetPreviewHidden(bool isHidden);
}
