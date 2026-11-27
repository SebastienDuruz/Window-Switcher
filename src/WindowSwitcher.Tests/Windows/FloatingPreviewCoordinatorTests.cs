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
            windowId => selectedWindowId = windowId,
            () => false
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
            _ => { },
            () => false
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
            windowId => selectedWindowId = windowId,
            () => false
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
            _ => { },
            () => false
        );

        sut.NotifyPreviewWindowActivated("window-1");

        Assert.Equal("window-1", activator.LastNotifiedWindowId);
    }

    [Fact]
    public void PreviewActivation_DoesNotHidePreview_WhenSettingIsDisabled()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => false);
        var preview = new FakeFloatingPreviewWindow();

        ActivateFromPreview(sut, preview, "window-1");

        Assert.False(preview.IsHidden);
        Assert.True(preview.IsHighlighted);
    }

    [Fact]
    public void PreviewActivation_HidesActivatedPreviewAndShowsPreviousOne_WhenSettingIsEnabled()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var first = new FakeFloatingPreviewWindow();
        var second = new FakeFloatingPreviewWindow();

        ActivateFromPreview(sut, first, "window-1");

        Assert.True(first.IsHidden);

        ActivateFromPreview(sut, second, "window-2");

        Assert.False(first.IsHidden);
        Assert.True(second.IsHidden);
    }

    [Fact]
    public void SetActivePreview_DoesNotHidePreview_BeforeActivationSucceeds()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var preview = new FakeFloatingPreviewWindow();

        sut.SetActivePreview(preview);

        Assert.False(preview.IsHidden);
    }

    [Fact]
    public void KeybindActivation_HidesSelectedPreview_WhenSettingIsEnabled()
    {
        var activator = new FakeWindowKeybindActivator();
        var preview = new FakeFloatingPreviewWindow();
        FloatingPreviewCoordinator? coordinator = null;
        using var sut = new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            _ => coordinator?.SetActivePreview(preview),
            () => true
        );
        coordinator = sut;

        activator.RaiseWindowActivated("window-1");

        Assert.True(preview.IsHidden);
        Assert.True(preview.IsHighlighted);
    }

    [Fact]
    public void ApplySettings_ShowsHiddenPreview_WhenSettingIsDisabled()
    {
        bool isEnabled = true;
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => isEnabled);
        var preview = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, preview, "window-1");

        isEnabled = false;
        sut.ApplySettings();

        Assert.False(preview.IsHidden);
    }

    [Fact]
    public void ApplySettings_DoesNotHideActivePreview_WhenSettingIsEnabled()
    {
        bool isEnabled = false;
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => isEnabled);
        var preview = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, preview, "window-1");

        isEnabled = true;
        sut.ApplySettings();

        Assert.False(preview.IsHidden);
    }

    [Fact]
    public void ClearActivePreview_ForgetsHiddenPreview()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var closed = new FakeFloatingPreviewWindow();
        var next = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, closed, "window-1");

        sut.ClearActivePreview(closed);
        int hiddenStateChangesBefore = closed.HiddenStateChangeCount;
        ActivateFromPreview(sut, next, "window-2");

        Assert.Equal(hiddenStateChangesBefore, closed.HiddenStateChangeCount);
        Assert.True(next.IsHidden);
    }

    [Fact]
    public void ActivePreviewChanged_ReportsTheHighlightedPreview()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => false);
        var first = new FakeFloatingPreviewWindow();
        var second = new FakeFloatingPreviewWindow();
        var reported = new List<IFloatingPreviewWindow?>();
        sut.ActivePreviewChanged += (_, preview) => reported.Add(preview);

        sut.SetActivePreview(first);
        sut.SetActivePreview(first);
        sut.SetActivePreview(second);
        sut.ClearActivePreview(first);
        sut.ClearActivePreview(second);

        Assert.Equal([first, second, null], reported);
    }

    [Fact]
    public void RevealPreview_ShowsTheHiddenPreview()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var preview = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, preview, "window-1");
        Assert.True(preview.IsHidden);

        sut.RevealPreview(preview);

        Assert.False(preview.IsHidden);
    }

    [Fact]
    public void RevealPreview_LeavesAVisiblePreviewUntouched()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var hidden = new FakeFloatingPreviewWindow();
        var visible = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, hidden, "window-1");

        sut.RevealPreview(visible);

        Assert.Equal(0, visible.HiddenStateChangeCount);
        Assert.True(hidden.IsHidden);
    }

    [Fact]
    public void RevealPreview_ThenActivatingAnotherWindow_KeepsTheRevealedPreviewShown()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var first = new FakeFloatingPreviewWindow();
        var second = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, first, "window-1");
        sut.RevealPreview(first);

        ActivateFromPreview(sut, second, "window-2");

        Assert.False(first.IsHidden);
        Assert.True(second.IsHidden);
    }

    [Fact]
    public void RevealPreview_ThenActivatingTheSameWindow_HidesItAgain()
    {
        using var sut = CreateCoordinator(new FakeWindowKeybindActivator(), () => true);
        var preview = new FakeFloatingPreviewWindow();
        ActivateFromPreview(sut, preview, "window-1");
        sut.RevealPreview(preview);

        sut.NotifyPreviewWindowActivated("window-1");

        Assert.True(preview.IsHidden);
    }

    private static FloatingPreviewCoordinator CreateCoordinator(
        FakeWindowKeybindActivator activator,
        Func<bool> isHideActivePreviewEnabled
    )
    {
        return new FloatingPreviewCoordinator(
            activator,
            new ImmediateViewModelDispatcher(),
            _ => { },
            isHideActivePreviewEnabled
        );
    }

    private static void ActivateFromPreview(
        FloatingPreviewCoordinator sut,
        FakeFloatingPreviewWindow preview,
        string windowId
    )
    {
        sut.SetActivePreview(preview);
        sut.NotifyPreviewWindowActivated(windowId);
    }

    private sealed class FakeFloatingPreviewWindow : IFloatingPreviewWindow
    {
        public bool IsHighlighted { get; private set; }

        public bool IsHidden { get; private set; }

        public int HiddenStateChangeCount { get; private set; }

        public void SetPreviewHighlight(bool isSelected)
        {
            IsHighlighted = isSelected;
        }

        public void SetPreviewHidden(bool isHidden)
        {
            IsHidden = isHidden;
            HiddenStateChangeCount++;
        }
    }

    private sealed class FakeWindowKeybindActivator : IWindowKeybindActivator
    {
        public event EventHandler<string>? WindowActivated;

        public string LastNotifiedWindowId { get; private set; } = string.Empty;

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
