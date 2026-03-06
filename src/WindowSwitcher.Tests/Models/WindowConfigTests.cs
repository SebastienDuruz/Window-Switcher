using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Models;

public sealed class WindowConfigTests
{
    [Fact]
    public void WindowTitle_UpdatesShortTitleAndRaisesNotifications()
    {
        var model = new WindowConfig();
        var raised = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                raised.Add(e.PropertyName);
        };

        model.WindowTitle = "A title";

        Assert.Equal("A title", model.ShortWindowTitle);
        Assert.Equal(new[] { "ShortWindowTitle", "WindowTitle" }, raised);
    }

    [Fact]
    public void WindowTitle_TruncatesShortTitle_WhenTitleIsLongerThanFortyCharacters()
    {
        var model = new WindowConfig();
        string title = "0123456789012345678901234567890123456789X";

        model.WindowTitle = title;

        Assert.Equal("0123456789012345678901234567890123456789...", model.ShortWindowTitle);
    }

    [Fact]
    public void WindowTitle_DoesNotRaiseEvent_WhenSameValueIsAssigned()
    {
        var model = new WindowConfig { WindowTitle = "Stable" };
        int raisedCount = 0;
        model.PropertyChanged += (_, _) => raisedCount++;

        model.WindowTitle = "Stable";

        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public void Clone_ReturnsIndependentCopy()
    {
        var original = new WindowConfig
        {
            WindowId = "w1",
            ProcessName = "proc",
            WindowTitle = "Editor",
            WindowWidth = 420,
        };

        WindowConfig clone = original.Clone();
        clone.WindowWidth = 300;
        clone.WindowTitle = "Terminal";

        Assert.NotSame(original, clone);
        Assert.Equal(420, original.WindowWidth);
        Assert.Equal("Editor", original.WindowTitle);
        Assert.Equal(300, clone.WindowWidth);
        Assert.Equal("Terminal", clone.WindowTitle);
    }
}
