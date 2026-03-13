using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class GlobalWindowKeybindRuntimeServiceTests
{
    [Fact]
    public async Task StartAndStop_AssignAndClearListenerFilter()
    {
        var listener = new FakeGlobalKeyboardListener();
        var manager = new FakeWindowKeybindManager();
        var activator = new FakeWindowKeybindActivator();
        await using var sut = new GlobalWindowKeybindRuntimeService(listener, manager, activator);

        await sut.StartAsync();
        Assert.Same(sut, listener.InputFilter);

        await sut.StopAsync();
        Assert.Null(listener.InputFilter);
    }

    [Fact]
    public async Task ProcessEvent_BuffersModifierAndConsumesMatchingCombination()
    {
        var listener = new FakeGlobalKeyboardListener();
        var manager = new FakeWindowKeybindManager();
        manager.SetBindings(
            ("builtin:next-client", new KeyCombination { Alt = true, Key = KeybindPrimaryKey.Tab })
        );
        var activator = new FakeWindowKeybindActivator();
        await using var sut = new GlobalWindowKeybindRuntimeService(listener, manager, activator);
        await sut.StartAsync();

        KeyboardFilterDecision altDown = sut.ProcessEvent(CreateWindowsEvent("VK_12", GlobalKeyState.Down));
        KeyboardFilterDecision tabDown = sut.ProcessEvent(CreateWindowsEvent("VK_09", GlobalKeyState.Down));
        KeyboardFilterDecision tabUp = sut.ProcessEvent(CreateWindowsEvent("VK_09", GlobalKeyState.Up));
        KeyboardFilterDecision altUp = sut.ProcessEvent(CreateWindowsEvent("VK_12", GlobalKeyState.Up));

        Assert.Equal(KeyboardEventRouting.Buffer, altDown.Routing);
        Assert.Equal(KeyboardEventRouting.Consume, tabDown.Routing);
        Assert.True(tabDown.DiscardBufferedEvents);
        Assert.Equal("builtin:next-client", tabDown.MatchedTargetId);
        Assert.Equal(KeyboardEventRouting.Consume, tabUp.Routing);
        Assert.Equal(KeyboardEventRouting.Consume, altUp.Routing);
        Assert.Equal(new[] { "builtin:next-client" }, activator.ActivatedTargetIds);
    }

    [Fact]
    public async Task ProcessEvent_FlushesBufferedPrefix_WhenCombinationDoesNotMatch()
    {
        var listener = new FakeGlobalKeyboardListener();
        var manager = new FakeWindowKeybindManager();
        manager.SetBindings(
            ("builtin:next-client", new KeyCombination { Alt = true, Key = KeybindPrimaryKey.Tab })
        );
        var activator = new FakeWindowKeybindActivator();
        await using var sut = new GlobalWindowKeybindRuntimeService(listener, manager, activator);
        await sut.StartAsync();

        KeyboardFilterDecision altDown = sut.ProcessEvent(CreateWindowsEvent("VK_12", GlobalKeyState.Down));
        KeyboardFilterDecision xDown = sut.ProcessEvent(CreateWindowsEvent("VK_58", GlobalKeyState.Down));
        KeyboardFilterDecision xUp = sut.ProcessEvent(CreateWindowsEvent("VK_58", GlobalKeyState.Up));
        KeyboardFilterDecision altUp = sut.ProcessEvent(CreateWindowsEvent("VK_12", GlobalKeyState.Up));

        Assert.Equal(KeyboardEventRouting.Buffer, altDown.Routing);
        Assert.Equal(KeyboardEventRouting.Forward, xDown.Routing);
        Assert.True(xDown.FlushBufferedEvents);
        Assert.True(xDown.ForwardCurrentEventViaForwarder);
        Assert.Equal(KeyboardEventRouting.Forward, xUp.Routing);
        Assert.Equal(KeyboardEventRouting.Forward, altUp.Routing);
        Assert.Empty(activator.ActivatedTargetIds);
    }

    [Fact]
    public async Task ProcessEvent_RefreshesBindingsAfterManagerChange()
    {
        var listener = new FakeGlobalKeyboardListener();
        var manager = new FakeWindowKeybindManager();
        var activator = new FakeWindowKeybindActivator();
        await using var sut = new GlobalWindowKeybindRuntimeService(listener, manager, activator);
        await sut.StartAsync();

        KeyboardFilterDecision beforeChange = sut.ProcessEvent(
            CreateWindowsEvent("VK_70", GlobalKeyState.Down)
        );

        manager.SetBindings(("builtin:previous-client", new KeyCombination { Key = KeybindPrimaryKey.F1 }));

        KeyboardFilterDecision afterChange = sut.ProcessEvent(
            CreateWindowsEvent("VK_70", GlobalKeyState.Down)
        );

        Assert.Equal(KeyboardEventRouting.Forward, beforeChange.Routing);
        Assert.Equal(KeyboardEventRouting.Consume, afterChange.Routing);
        Assert.Equal("builtin:previous-client", afterChange.MatchedTargetId);
    }

    private static GlobalKeyEventArgs CreateWindowsEvent(
        string keyCode,
        GlobalKeyState state,
        bool isRepeat = false)
    {
        return new GlobalKeyEventArgs
        {
            Platform = "Windows",
            KeyCode = keyCode,
            State = state,
            IsRepeat = isRepeat,
        };
    }

    private sealed class FakeGlobalKeyboardListener : IGlobalKeyboardListener
    {
        public IKeyboardInputFilter? InputFilter { get; set; }

#pragma warning disable CS0067
        public event EventHandler<GlobalKeyEventArgs>? KeyEvent;
#pragma warning restore CS0067

        public bool IsRunning { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWindowKeybindManager : IWindowKeybindManager
    {
        private WindowKeybindTargetConfig[] _targets = [];

        public event EventHandler? BindingsChanged;

        public IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets()
        {
            return _targets;
        }

        public IReadOnlyCollection<WindowKeybindShortcut> GetShortcutsForTarget(string targetId)
        {
            ArgumentNullException.ThrowIfNull(targetId);
            return _targets
                .FirstOrDefault(target => string.Equals(target.TargetId, targetId, StringComparison.Ordinal))
                ?.Shortcuts
                ?? [];
        }

        public void UpsertTarget(string targetId, string displayLabel) { }

        public KeybindShortcutAddResult AddShortcut(
            string targetId,
            string displayLabel,
            KeyCombination combination)
        {
            throw new NotSupportedException();
        }

        public bool RemoveShortcut(string targetId, KeyCombination combination)
        {
            throw new NotSupportedException();
        }

        public bool TryResolveTarget(KeyCombination combination, out string targetId)
        {
            targetId = string.Empty;
            return false;
        }

        public void SetBindings(params (string TargetId, KeyCombination Combination)[] bindings)
        {
            _targets = bindings
                .GroupBy(binding => binding.TargetId, StringComparer.Ordinal)
                .Select(group => new WindowKeybindTargetConfig
                {
                    TargetId = group.Key,
                    DisplayLabel = group.Key,
                    Shortcuts = group
                        .Select(binding => new WindowKeybindShortcut
                        {
                            Combination = binding.Combination.Clone(),
                        })
                        .ToList(),
                })
                .ToArray();

            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeWindowKeybindActivator : IWindowKeybindActivator
    {
        public event EventHandler<string>? WindowActivated;

        public List<string> ActivatedTargetIds { get; } = [];

        public bool TryActivateTarget(string targetId)
        {
            ActivatedTargetIds.Add(targetId);
            WindowActivated?.Invoke(this, targetId);
            return true;
        }

        public void NotifyWindowActivated(string windowId) { }
    }
}
