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
            () => Array.Empty<WindowConfig>()
        );

        Assert.True(sut.ShowGlobalKeyboardSetupBanner);
        Assert.Equal("Global shortcuts unavailable", sut.GlobalKeyboardSetupTitle);
        Assert.False(sut.ShowGlobalKeyboardSetupCommand);
        Assert.Equal(string.Empty, sut.GlobalKeyboardSetupCommand);
    }

    private sealed class FakeWindowKeybindManager : IWindowKeybindManager
    {
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

    private sealed class FakeWindowKeybindTargetCatalogService : IWindowKeybindTargetCatalogService
    {
        public KeybindTargetCatalogSnapshot GetTargets(IReadOnlyCollection<WindowConfig> runtimeWindows)
        {
            return new KeybindTargetCatalogSnapshot([], []);
        }
    }
}
