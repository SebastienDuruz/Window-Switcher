using System.Collections.ObjectModel;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class WindowKeybindActivatorTests
{
    [Fact]
    public async Task TryActivateTarget_NextClient_CyclesForward()
    {
        var accessor = new FakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));

        Assert.Equal(new[] { "w-1", "w-2", "w-3", "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_PreviousClient_CyclesBackward()
    {
        var accessor = new FakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.PreviousClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.PreviousClientTargetId));

        Assert.Equal(new[] { "w-3", "w-2" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_WindowTarget_StillRaisesMatchingWindow()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        var accessor = new FakeWinAccessor(editor, terminal);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        string editorTargetId = WindowTargetKeyFactory.Create(editor);
        bool activated = await sut.TryActivateTargetAsync(editorTargetId);

        Assert.True(activated);
        Assert.Equal(new[] { "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_WindowTarget_RaisesWindowActivatedEvent()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        var accessor = new FakeWinAccessor(editor);
        var sut = new WindowKeybindActivator(accessor, SelectAll);
        string? activatedWindowId = null;
        sut.WindowActivated += (_, windowId) => activatedWindowId = windowId;

        bool activated = await sut.TryActivateTargetAsync(WindowTargetKeyFactory.Create(editor));

        Assert.True(activated);
        Assert.Equal("w-1", activatedWindowId);
    }

    [Fact]
    public async Task TryActivateTarget_ReturnsFalseWhenAccessorRejectsActivation()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        var accessor = new FakeWinAccessor(editor) { CanActivate = false };
        var sut = new WindowKeybindActivator(accessor, SelectAll);
        string? activatedWindowId = null;
        sut.WindowActivated += (_, windowId) => activatedWindowId = windowId;

        bool activated = await sut.TryActivateTargetAsync(WindowTargetKeyFactory.Create(editor));

        Assert.False(activated);
        Assert.Null(activatedWindowId);
        Assert.Empty(accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_BuiltInAction_RaisesWindowActivatedEvent()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        var accessor = new FakeWinAccessor(editor, terminal);
        var sut = new WindowKeybindActivator(accessor, SelectAll);
        string? activatedWindowId = null;
        sut.WindowActivated += (_, windowId) => activatedWindowId = windowId;

        bool activated = await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId);

        Assert.True(activated);
        Assert.Equal("w-1", activatedWindowId);
    }

    [Fact]
    public async Task TryActivateTarget_FocusActiveClient_RaisesLastActivatedWindow()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        var accessor = new FakeWinAccessor(editor, terminal);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        _ = await sut.TryActivateTargetAsync(WindowTargetKeyFactory.Create(terminal));

        bool activated = await sut.TryActivateTargetAsync(KeybindBuiltInTargets.FocusActiveClientTargetId);

        Assert.True(activated);
        Assert.Equal(new[] { "w-2", "w-2" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_FocusActiveClient_ReturnsFalseWhenNoActiveClient()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        var accessor = new FakeWinAccessor(editor);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        bool activated = await sut.TryActivateTargetAsync(KeybindBuiltInTargets.FocusActiveClientTargetId);

        Assert.False(activated);
        Assert.Empty(accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_NextClient_UsesLastActivatedWindowAsAnchor()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        WindowConfig browser = CreateWindow("w-3", "Browser", "firefox");
        var accessor = new FakeWinAccessor(editor, terminal, browser);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        _ = await sut.TryActivateTargetAsync(WindowTargetKeyFactory.Create(terminal));
        _ = await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId);

        Assert.Equal(new[] { "w-2", "w-3" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task NotifyWindowActivated_UpdatesAnchorForNextClient()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        WindowConfig browser = CreateWindow("w-3", "Browser", "firefox");
        var accessor = new FakeWinAccessor(editor, terminal, browser);
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        sut.NotifyWindowActivated("w-2");

        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.Equal(new[] { "w-3" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_BuiltInActions_ReturnFalseWhenNoClientsAvailable()
    {
        var accessor = new FakeWinAccessor();
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.False(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.False(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.PreviousClientTargetId));
        Assert.Empty(accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_NextClient_UsesOnlySelectedClients()
    {
        WindowConfig editor = CreateWindow("w-1", "Editor", "code");
        WindowConfig terminal = CreateWindow("w-2", "Terminal", "wezterm");
        WindowConfig browser = CreateWindow("w-3", "Browser", "firefox");
        var accessor = new FakeWinAccessor(editor, terminal, browser);
        var sut = new WindowKeybindActivator(
            accessor,
            windows => windows.Where(window => window.WindowId is "w-1" or "w-3").ToArray()
        );

        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));

        Assert.Equal(new[] { "w-1", "w-3", "w-1" }, accessor.RaisedWindowIds);
    }

    [Fact]
    public async Task TryActivateTarget_NextClient_KeepsStableOrderWhenAccessorReordersRaisedWindowFirst()
    {
        var accessor = new ReorderingFakeWinAccessor(
            CreateWindow("w-1", "Editor", "code"),
            CreateWindow("w-2", "Terminal", "wezterm"),
            CreateWindow("w-3", "Browser", "firefox")
        );
        var sut = new WindowKeybindActivator(accessor, SelectAll);

        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));
        Assert.True(await sut.TryActivateTargetAsync(KeybindBuiltInTargets.NextClientTargetId));

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
        public bool CanActivate { get; init; } = true;

        public override Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyCollection<WindowConfig>>(_windows);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        )
        {
            if (!CanActivate)
                return Task.FromResult(false);

            RaisedWindowIds.Add(windowId);
            return Task.FromResult(true);
        }

        public override Task<bool> TryRenameWindowAsync(
            string windowId,
            string windowTitle,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }

    private sealed class ReorderingFakeWinAccessor(params WindowConfig[] windows) : WinAccessorBase
    {
        private readonly ObservableCollection<WindowConfig> _windows = new(windows);

        public List<string> RaisedWindowIds { get; } = [];

        public override Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyCollection<WindowConfig>>(_windows);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        )
        {
            RaisedWindowIds.Add(windowId);

            WindowConfig? target = _windows.FirstOrDefault(window =>
                string.Equals(window.WindowId, windowId, StringComparison.Ordinal)
            );
            if (target is null)
                return Task.FromResult(true);

            _windows.Remove(target);
            _windows.Insert(0, target);
            return Task.FromResult(true);
        }

        public override Task<bool> TryRenameWindowAsync(
            string windowId,
            string windowTitle,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }

    private static IReadOnlyList<WindowConfig> SelectAll(IReadOnlyCollection<WindowConfig> windows)
    {
        return windows.ToArray();
    }
}
