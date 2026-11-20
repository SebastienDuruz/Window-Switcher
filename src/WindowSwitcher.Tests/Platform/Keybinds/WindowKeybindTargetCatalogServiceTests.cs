using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;
using Xunit;
using static WindowSwitcher.Tests.TestConfig.ConfigFileAccessorTestFactory;

namespace WindowSwitcher.Tests.Platform.Keybinds;

[Collection(ConfigFileAccessorIsolationCollection.Name)]
public sealed class WindowKeybindTargetCatalogServiceTests
{
    [Fact]
    public void GetTargets_ReturnsBuiltIns_RuntimeTargets_AndPersistedOrphans()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            ConfigFileAccessor accessor = CreateAccessor(dataFolder);
            var manager = new WindowKeybindManager(accessor);
            var service = new WindowKeybindTargetCatalogService(manager);

            accessor.SaveWindowKeybindTargets([
                new WindowKeybindTargetConfig
                {
                    TargetId = KeybindBuiltInTargets.NextClientTargetId,
                    DisplayLabel = "Next focus",
                    Shortcuts = [],
                },
                new WindowKeybindTargetConfig
                {
                    TargetId = "proc|persisted",
                    DisplayLabel = "Persisted Window",
                    Shortcuts = [],
                },
            ]);

            KeybindTargetCatalogSnapshot snapshot = service.GetTargets([
                new WindowConfig
                {
                    WindowId = "w-1",
                    ProcessName = "proc",
                    WindowTitle = "Editor",
                },
            ]);

            Assert.Contains(
                snapshot.ActionTargets,
                target =>
                    target.TargetId == KeybindBuiltInTargets.NextClientTargetId
                    && target.DisplayName == "Next focus"
            );
            Assert.Contains(
                snapshot.ActionTargets,
                target => target.TargetId == KeybindBuiltInTargets.PreviousClientTargetId
            );
            Assert.Contains(
                snapshot.ActionTargets,
                target =>
                    target.TargetId == KeybindBuiltInTargets.FocusActiveClientTargetId
                    && target.DisplayName == KeybindBuiltInTargets.FocusActiveClientDisplayName
            );
            Assert.Contains(
                snapshot.ClientTargets,
                target =>
                    target.TargetId == WindowTargetKeyFactory.Create("proc", "Editor")
                    && target.DisplayName == "Editor (proc)"
            );
            Assert.Contains(
                snapshot.ClientTargets,
                target =>
                    target.TargetId == "proc|persisted" && target.DisplayName == "Persisted Window"
            );
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }
}
