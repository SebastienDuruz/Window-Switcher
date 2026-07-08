using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
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
        Assert.Contains("/dev/input/event*", sut.GlobalKeyboardSetupMessage, StringComparison.Ordinal);
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
    public void TryCaptureKey_UsesUiIndependentCaptureResult()
    {
        var keybindManager = new FakeWindowKeybindManager();
        var sut = new KeybindSettingsViewModel(
            keybindManager,
            new FakeWindowKeybindTargetCatalogService(
                new KeybindTargetCatalogSnapshot(
                    [
                        new KeybindTargetDescriptor(
                            "show-next",
                            "Show next",
                            isBuiltIn: true
                        ),
                    ],
                    []
                )
            ),
            new GlobalKeyboardStartupStatusService(),
            new ImmediateViewModelDispatcher(),
            () => Array.Empty<WindowConfig>()
        );
        sut.SelectedTarget = sut.ActionTargets.Single();
        sut.BeginCaptureCommand.Execute(null);

        bool handled = sut.TryCaptureKey(
            KeybindCaptureResult.Success(
                new KeyCombination
                {
                    Ctrl = true,
                    Key = KeybindPrimaryKey.A,
                }
            )
        );
        sut.ConfirmCaptureCommand.Execute(null);

        Assert.True(handled);
        Assert.Equal("show-next", keybindManager.LastAddedTargetId);
        Assert.NotNull(keybindManager.LastAddedCombination);
        Assert.True(keybindManager.LastAddedCombination!.Ctrl);
        Assert.Equal(KeybindPrimaryKey.A, keybindManager.LastAddedCombination.Key);
    }

    private sealed class FakeWindowKeybindManager : IWindowKeybindManager
    {
        public string? LastAddedTargetId { get; private set; }
        public KeyCombination? LastAddedCombination { get; private set; }

        public event EventHandler? BindingsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyCollection<WindowKeybindTargetConfig> GetTargets()
        {
            return [];
        }

        public IReadOnlyCollection<WindowKeybindShortcut> GetShortcutsForTarget(string targetId)
        {
            return [];
        }

        public void UpsertTarget(string targetId, string displayLabel) { }

        public KeybindShortcutAddResult AddShortcut(
            string targetId,
            string displayLabel,
            KeyCombination combination
        )
        {
            LastAddedTargetId = targetId;
            LastAddedCombination = combination.Clone();
            return KeybindShortcutAddResult.Added();
        }

        public bool RemoveShortcut(string targetId, KeyCombination combination)
        {
            return false;
        }

        public bool TryResolveTarget(KeyCombination combination, out string targetId)
        {
            targetId = string.Empty;
            return false;
        }
    }

    private sealed class FakeWindowKeybindTargetCatalogService(
        KeybindTargetCatalogSnapshot? snapshot = null)
        : IWindowKeybindTargetCatalogService
    {
        public KeybindTargetCatalogSnapshot GetTargets(IReadOnlyCollection<WindowConfig> runtimeWindows)
        {
            return snapshot ?? new KeybindTargetCatalogSnapshot([], []);
        }
    }
}
