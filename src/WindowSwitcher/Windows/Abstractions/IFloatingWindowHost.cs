using System.Threading.Tasks;

namespace WindowSwitcher.Windows.Abstractions;

public interface IFloatingWindowHost
{
    void AddToBlacklist(string windowTitle);
    void AddToTempBlacklist(string windowId);
    void SetActivePreview(IFloatingPreviewWindow floatingWindow);
    void ClearActivePreview(IFloatingPreviewWindow floatingWindow);
    void NotifyPreviewWindowActivated(string windowId);
    Task RenameWindowTitleAsync(string windowId);
}
