using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class WindowKeybindActivatorTests
{
    [Fact]
    public void TryActivateTarget_NextClient_CyclesForward()
    {
        var accessor = new FakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));

        Assert.Equal(new[] { "w-1", "w-2", "w-3", "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_PreviousClient_CyclesBackward()
    {
        var accessor = new FakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.PreviousClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.PreviousClientTargetId));

        Assert.Equal(new[] { "w-3", "w-2" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_WindowTarget_StillRaisesMatchingWindow()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        var accessor = new FakeWinAccessor(editor, terminal);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        string editorTargetId = WindowTargetKeyFactory.Create(editor);
        bool activated = sut.TryActivateTarget(editorTargetId);

        Assert.True(activated);
        Assert.Equal(new[] { "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_WindowTarget_RaisesWindowActivatedEvent()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        var accessor = new FakeWinAccessor(editor);
        var sut = new WindowKeybindActivator(accessor, SelectAll);
        string? activatedWindowId = null;
        sut.WindowActivated += (_, windowId) => activatedWindowId = windowId;

        bool activated = sut.TryActivateTarget(WindowTargetKeyFactory.Create(editor));

        Assert.True(activated);
        Assert.Equal("w-1", activatedWindowId);
    }

    [Fact]
    public void TryActivateTarget_BuiltInAction_RaisesWindowActivatedEvent()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        var accessor = new FakeWinAccessor(editor, terminal);
        var sut = new WindowKeybindActivator(accessor, SelectAll);
        string? activatedWindowId = null;
        sut.WindowActivated += (_, windowId) => activatedWindowId = windowId;

        bool activated = sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId);

        Assert.True(activated);
        Assert.Equal("w-1", activatedWindowId);
    }

    [Fact]
    public void TryActivateTarget_NextClient_UsesLastActivatedWindowAsAnchor()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        WindowConfig browser = CreateWindow("w-3", "Browser", "firefox");
        var accessor = new FakeWinAccessor(editor, terminal, browser);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        _ = sut.TryActivateTarget(WindowTargetKeyFactory.Create(terminal));
        _ = sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId);

        Assert.Equal(new[] { "w-2", "w-3" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_BuiltInActions_ReturnFalseWhenNoClientsAvailable()
    {
        var accessor = new FakeWinAccessor();
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.False(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.False(sut.TryActivateTarget(KeybindBuiltInTargets.PreviousClientTargetId));
        Assert.Empty(accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_NextClient_UsesOnlySelectedClients()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        WindowConfig browser = CreateWindow("w-3", "Browser", "firefox");
        var accessor = new FakeWinAccessor(editor, terminal, browser);
        var sut = new WindowKeybindActivator(accessor, windows =>
            windows.Where(window => window.WindowId is "w-1" or "w-3").ToArray()
        );

        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));

        Assert.Equal(new[] { "w-1", "w-3", "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public void TryActivateTarget_NextClient_KeepsStableOrderWhenAccessorReordersRaisedWindowFirst()
    {
        var accessor = new ReorderingFakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(sut.TryActivateTarget(KeybindBuiltInTargets.NextClientTargetId));

        Assert.Equal(new[] { "w-1", "w-2", "w-3", "w-1" }, accessor.RaisedWindowIds);
    }

    private static WindowConfig CreateWindow(string id, string title, string process)
    {
        return new WindowConfig
        {
            WindowId = id,
            WindowTitle = title,
            ProcessName = process,
        };
    }

    private sealed class FakeWinAccessor(params WindowConfig[] windows) : WinAccessorBase
    {
        private readonly ObservableCollection<WindowConfig> _windows = new(windows);

        public List<string> RaisedWindowIds { get; } = [];

        public override ObservableCollection<WindowConfig> GetWindows()
        {
            return _windows;
        }

        public override void RaiseWindow(string windowId)
        {
            RaisedWindowIds.Add(windowId);
        }

        public override Bitmap? TakeScreenshot(string windowId)
        {
            return null;
        }

        public override void RenameWindowTitle(string windowId, string windowTitle) { }
    }

    private sealed class ReorderingFakeWinAccessor(params WindowConfig[] windows) : WinAccessorBase
    {
        private readonly ObservableCollection<WindowConfig> _windows = new(windows);

        public List<string> RaisedWindowIds { get; } = [];

        public override ObservableCollection<WindowConfig> GetWindows()
        {
            return _windows;
        }

        public override void RaiseWindow(string windowId)
        {
            RaisedWindowIds.Add(windowId);

            WindowConfig? target = _windows.FirstOrDefault(window =>
                string.Equals(window.WindowId, windowId, StringComparison.Ordinal)
            );
            if (target is null)
                return;

            _windows.Remove(target);
            _windows.Insert(0, target);
        }

        public override Bitmap? TakeScreenshot(string windowId)
        {
            return null;
        }

        public override void RenameWindowTitle(string windowId, string windowTitle) { }
    }

    private static IReadOnlyList<WindowConfig> SelectAll(IReadOnlyCollection<WindowConfig> windows)
    {
        return windows.ToArray();
    }
}
