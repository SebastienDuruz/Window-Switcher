using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class KeybindSettingsViewModelTests
{
    [Fact]
    public void Constructor_ShowsLinuxAccessBanner_WhenGlobalKeyboardNeedsInputAccess()
    {
        var statusService = new GlobalKeyboardStartupStatusService();
        statusService.ReportStartupFailure(new LinuxInputAccessException(["/dev/input/event0"]));

        var sut = new KeybindSettingsViewModel(
            new FakeWindowKeybindManager(),
            new FakeWindowKeybindTargetCatalogService(),
            statusService,
            new ImmediateViewModelDispatcher(),
            () => Array.Empty<WindowConfig>()
        );

        Assert.True(sut.ShowGlobalKeyboardSetupBanner);
        Assert.Equal("Linux input access required", sut.GlobalKeyboardSetupTitle);
        Assert.Contains(
            "/dev/input/event*",
            sut.GlobalKeyboardSetupMessage,
            StringComparison.Ordinal
        );
        Assert.Equal("sudo usermod -aG input \"$USER\"", sut.GlobalKeyboardSetupCommand);
        Assert.True(sut.ShowGlobalKeyboardSetupCommand);
        Assert.Contains("Sign out", sut.GlobalKeyboardSetupGuidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_HidesSetupCommand_ForGenericStartupFailure()
    {
        var statusService = new GlobalKeyboardStartupStatusService();
        statusService.ReportStartupFailure(new InvalidOperationException("boom"));

        var sut = new KeybindSettingsViewModel(
            new FakeWindowKeybindManager(),
            new FakeWindowKeybindTargetCatalogService(),
            statusService,
            new ImmediateViewModelDispatcher(),
            () => Array.Empty<WindowConfig>()
        );

        Assert.True(sut.ShowGlobalKeyboardSetupBanner);
        Assert.Equal("Global shortcuts unavailable", sut.GlobalKeyboardSetupTitle);
        Assert.False(sut.ShowGlobalKeyboardSetupCommand);
        Assert.Equal(string.Empty, sut.GlobalKeyboardSetupCommand);
    }

    [Fact]
    public void Constructor_BuildsOneRowPerTargetWithItsShortcuts()
    {
        var keybindManager = new FakeWindowKeybindManager();
        keybindManager.Seed("show-next", new KeyCombination { Key = KeybindPrimaryKey.F2 });
        keybindManager.Seed("show-next", new KeyCombination { Key = KeybindPrimaryKey.F1 });

        var sut = CreateSut(keybindManager);

        KeybindTargetRowViewModel action = Assert.Single(sut.ActionRows);
        Assert.Equal("Show next", action.DisplayName);
        Assert.Equal(["F1", "F2"], action.Shortcuts.Select(shortcut => shortcut.Display));
        KeybindTargetRowViewModel client = Assert.Single(sut.ClientRows);
        Assert.Equal("Editor (code)", client.DisplayName);
        Assert.Empty(client.Shortcuts);
        Assert.True(sut.HasClientRows);
    }

    [Fact]
    public void HasClientRows_IsFalseWithoutClientTargets()
    {
        var sut = CreateSut(
            new FakeWindowKeybindManager(),
            new KeybindTargetCatalogSnapshot([ShowNext], [])
        );

        Assert.False(sut.HasClientRows);
    }

    [Fact]
    public void TryCaptureKey_IsIgnoredWithoutACaptureInProgress()
    {
        var sut = CreateSut(new FakeWindowKeybindManager());

        bool handled = sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA));

        Assert.False(handled);
    }

    [Fact]
    public void BeginCapture_OpensTheCaptureOnThatRowOnly()
    {
        var sut = CreateSut(new FakeWindowKeybindManager());
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        KeybindTargetRowViewModel client = sut.ClientRows.Single();

        action.BeginCaptureCommand.Execute(null);

        Assert.True(action.IsCapturing);
        Assert.False(action.BeginCaptureCommand.CanExecute(null));
        Assert.False(action.ConfirmCaptureCommand.CanExecute(null));
        Assert.Same(action, sut.CapturingRow);

        client.BeginCaptureCommand.Execute(null);

        Assert.False(action.IsCapturing);
        Assert.True(client.IsCapturing);
        Assert.Same(client, sut.CapturingRow);
    }

    [Fact]
    public void ConfirmCapture_AddsTheShortcutToTheRowAndReportsSuccess()
    {
        var keybindManager = new FakeWindowKeybindManager();
        var sut = CreateSut(keybindManager);
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        action.BeginCaptureCommand.Execute(null);

        bool handled = sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA));

        Assert.True(handled);
        Assert.Equal("Ctrl+A", action.CapturePreview);
        Assert.True(action.ConfirmCaptureCommand.CanExecute(null));

        action.ConfirmCaptureCommand.Execute(null);

        Assert.Equal("show-next", keybindManager.LastAddedTargetId);
        Assert.Equal(["Ctrl+A"], action.Shortcuts.Select(shortcut => shortcut.Display));
        Assert.False(action.IsCapturing);
        Assert.Null(sut.CapturingRow);
        Assert.Equal("Shortcut added to Show next.", sut.StatusMessage);
        Assert.True(sut.IsStatusSuccess);
        Assert.False(sut.IsStatusError);
    }

    [Fact]
    public void ConfirmCapture_KeepsTheCaptureOpenAndReportsAnErrorWhenRejected()
    {
        var keybindManager = new FakeWindowKeybindManager
        {
            NextAddResult = KeybindShortcutAddResult.Conflict("Already used by Editor."),
        };
        var sut = CreateSut(keybindManager);
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        action.BeginCaptureCommand.Execute(null);
        _ = sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA));

        action.ConfirmCaptureCommand.Execute(null);

        Assert.True(action.IsCapturing);
        Assert.Empty(action.Shortcuts);
        Assert.Equal("Already used by Editor.", sut.StatusMessage);
        Assert.True(sut.IsStatusError);
    }

    [Fact]
    public void TryCaptureKey_ShowsAFailedCaptureInTheRow()
    {
        var sut = CreateSut(new FakeWindowKeybindManager());
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        action.BeginCaptureCommand.Execute(null);
        _ = sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA));

        bool handled = sut.TryCaptureKey(KeybindCaptureResult.Failure("Unsupported key."));

        Assert.True(handled);
        Assert.Equal("Unsupported key.", action.CapturePreview);
        Assert.False(action.ConfirmCaptureCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCapture_ClosesTheCaptureWithoutAdding()
    {
        var keybindManager = new FakeWindowKeybindManager();
        var sut = CreateSut(keybindManager);
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        action.BeginCaptureCommand.Execute(null);
        _ = sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA));

        action.CancelCaptureCommand.Execute(null);

        Assert.False(action.IsCapturing);
        Assert.Equal(KeybindTargetRowViewModel.CapturePrompt, action.CapturePreview);
        Assert.Null(keybindManager.LastAddedTargetId);
        Assert.False(sut.TryCaptureKey(KeybindCaptureResult.Success(CtrlA)));
    }

    [Fact]
    public void RemoveShortcut_RemovesItFromTheRowAndReportsSuccess()
    {
        var keybindManager = new FakeWindowKeybindManager();
        keybindManager.Seed("show-next", CtrlA);
        var sut = CreateSut(keybindManager);
        KeybindTargetRowViewModel action = sut.ActionRows.Single();

        action.RemoveShortcutCommand.Execute(action.Shortcuts.Single());

        Assert.Empty(action.Shortcuts);
        Assert.Equal("Shortcut removed from Show next.", sut.StatusMessage);
        Assert.True(sut.IsStatusSuccess);
    }

    [Fact]
    public void RemoveShortcut_ReportsAnErrorWhenItFails()
    {
        var keybindManager = new FakeWindowKeybindManager();
        keybindManager.Seed("show-next", CtrlA);
        var sut = CreateSut(keybindManager);
        KeybindTargetRowViewModel action = sut.ActionRows.Single();
        keybindManager.FailRemovals = true;

        action.RemoveShortcutCommand.Execute(action.Shortcuts.Single());

        Assert.Single(action.Shortcuts);
        Assert.Equal("Unable to remove this shortcut.", sut.StatusMessage);
        Assert.True(sut.IsStatusError);
    }

    [Fact]
    public void RefreshTargets_CancelsTheCaptureAndReloadsTheRows()
    {
        var keybindManager = new FakeWindowKeybindManager();
        var sut = CreateSut(keybindManager);
        sut.ActionRows.Single().BeginCaptureCommand.Execute(null);
        keybindManager.Seed("show-next", CtrlA);

        sut.RefreshTargetsCommand.Execute(null);

        Assert.Null(sut.CapturingRow);
        Assert.Equal(["Ctrl+A"], sut.ActionRows.Single().Shortcuts.Select(s => s.Display));
    }

    private static readonly KeybindTargetDescriptor ShowNext = new(
        "show-next",
        "Show next",
        isBuiltIn: true
    );

    private static readonly KeybindTargetDescriptor Editor = new(
        "code|editor",
        "Editor (code)",
        isBuiltIn: false
    );

    private static readonly KeyCombination CtrlA = new() { Ctrl = true, Key = KeybindPrimaryKey.A };

    private static KeybindSettingsViewModel CreateSut(
        FakeWindowKeybindManager keybindManager,
        KeybindTargetCatalogSnapshot? catalog = null
    )
    {
        return new KeybindSettingsViewModel(
            keybindManager,
            new FakeWindowKeybindTargetCatalogService(
                catalog ?? new KeybindTargetCatalogSnapshot([ShowNext], [Editor])
            ),
            new GlobalKeyboardStartupStatusService(),
            new ImmediateViewModelDispatcher(),
            () => Array.Empty<WindowConfig>()
        );
    }

    private sealed class FakeWindowKeybindManager : IWindowKeybindManager
    {
        private readonly Dictionary<string, List<KeyCombination>> _shortcuts = new(
            StringComparer.Ordinal
        );

        public string? LastAddedTargetId { get; private set; }
        public KeybindShortcutAddResult? NextAddResult { get; init; }
        public bool FailRemovals { get; set; }

        public event EventHandler? BindingsChanged
        {
            add { }
            remove { }
        }

        public void Seed(string targetId, KeyCombination combination)
        {
            if (!_shortcuts.TryGetValue(targetId, out List<KeyCombination>? list))
                _shortcuts[targetId] = list = [];
            list.Add(combination.Clone());
        }

        public IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets()
        {
            return [];
        }

        public IReadOnlyCollection<WindowKeybindShortcut> GetShortcutsForTarget(string targetId)
        {
            return _shortcuts.TryGetValue(targetId, out List<KeyCombination>? list)
                ? list.Select(combination => new WindowKeybindShortcut
                    {
                        Combination = combination.Clone(),
                    })
                    .ToArray()
                : [];
        }

        public void UpsertTarget(string targetId, string displayLabel) { }

        public KeybindShortcutAddResult AddShortcut(
            string targetId,
            string displayLabel,
            KeyCombination combination
        )
        {
            if (NextAddResult is { Succeeded: false })
                return NextAddResult;

            LastAddedTargetId = targetId;
            Seed(targetId, combination);
            return KeybindShortcutAddResult.Added();
        }

        public bool RemoveShortcut(string targetId, KeyCombination combination)
        {
            if (FailRemovals || !_shortcuts.TryGetValue(targetId, out List<KeyCombination>? list))
                return false;

            return list.RemoveAll(existing =>
                    KeyCombinationParser.ToCanonicalString(existing)
                    == KeyCombinationParser.ToCanonicalString(combination)
                ) > 0;
        }

        public bool TryResolveTarget(KeyCombination combination, out string targetId)
        {
            targetId = string.Empty;
            return false;
        }

        public void NotifyConfigurationReplaced() { }
    }

    private sealed class FakeWindowKeybindTargetCatalogService(
        KeybindTargetCatalogSnapshot? snapshot = null
    ) : IWindowKeybindTargetCatalogService
    {
        public KeybindTargetCatalogSnapshot GetTargets(
            IReadOnlyCollection<WindowConfig> runtimeWindows
        )
        {
            return snapshot ?? new KeybindTargetCatalogSnapshot([], []);
        }
    }
}
