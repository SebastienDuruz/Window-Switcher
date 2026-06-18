using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Windows.Abstractions;
using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class FloatingPreviewCoordinatorTests
{
    [Fact]
    public void SetActivePreview_UpdatesHighlight()
    {
        var activator = new FakeWindowKeybindActivator();
        string selectedWindowId = string.Empty;
        using var sut = new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            windowId => selectedWindowId = windowId
        );
        var first = new FakeFloatingPreviewWindow();
        var second = new FakeFloatingPreviewWindow();

        sut.SetActivePreview(first);
        sut.SetActivePreview(second);

        Assert.False(first.IsHighlighted);
        Assert.True(second.IsHighlighted);
        Assert.Equal(string.Empty, selectedWindowId);
    }

    [Fact]
    public void ClearActivePreview_ClearsOnlyCurrentPreview()
    {
        var activator = new FakeWindowKeybindActivator();
        using var sut = new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            _ => { }
        );
        var first = new FakeFloatingPreviewWindow();
        var second = new FakeFloatingPreviewWindow();

        sut.SetActivePreview(first);
        sut.ClearActivePreview(second);
        sut.ClearActivePreview(first);

        Assert.False(first.IsHighlighted);
    }

    [Fact]
    public void KeybindActivation_SelectsPreviewByWindowId()
    {
        var activator = new FakeWindowKeybindActivator();
        string selectedWindowId = string.Empty;
        using var sut = new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            windowId => selectedWindowId = windowId
        );

        activator.RaiseWindowActivated("window-1");

        Assert.Equal("window-1", selectedWindowId);
    }

    [Fact]
    public void NotifyPreviewWindowActivated_ForwardsActivationAnchor()
    {
        var activator = new FakeWindowKeybindActivator();
        using var sut = new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            _ => { }
        );

        sut.NotifyPreviewWindowActivated("window-1");

        Assert.Equal("window-1", activator.LastNotifiedWindowId);
    }

    private sealed class FakeFloatingPreviewWindow : IFloatingPreviewWindow
    {
        public bool IsHighlighted { get; private set; }

        public void SetPreviewHighlight(bool isSelected)
        {
            IsHighlighted = isSelected;
        }
    }

    private sealed class FakeWindowKeybindActivator : IWindowKeybindActivator
    {
        public event EventHandler<string>? WindowActivated;

        public string LastNotifiedWindowId { get; private set; } = string.Empty;

        public bool TryActivateTarget(string targetId)
        {
            return true;
        }

        public Task<bool> TryActivateTargetAsync(
            string targetId,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(true);
        }

        public void NotifyWindowActivated(string windowId)
        {
            LastNotifiedWindowId = windowId;
        }

        public void RaiseWindowActivated(string windowId)
        {
            WindowActivated?.Invoke(this, windowId);
        }
    }
}
