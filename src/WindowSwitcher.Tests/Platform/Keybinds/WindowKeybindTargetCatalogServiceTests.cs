using System.Reflection;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

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

            accessor.SaveWindowKeybindTargets(
                [
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
                ]
            );

            KeybindTargetCatalogSnapshot snapshot = service.GetTargets(
                [
                    new WindowConfig
                    {
                        WindowId = "w-1",
                        ProcessName = "proc",
                        WindowTitle = "Editor",
                    },
                ]
            );

            Assert.Contains(snapshot.ActionTargets, target =>
                target.TargetId == KeybindBuiltInTargets.NextClientTargetId
                && target.DisplayName == "Next focus"
            );
            Assert.Contains(snapshot.ActionTargets, target =>
                target.TargetId == KeybindBuiltInTargets.PreviousClientTargetId
            );
            Assert.Contains(snapshot.ClientTargets, target =>
                target.TargetId == WindowTargetKeyFactory.Create("proc", "Editor")
                && target.DisplayName == "Editor (proc)"
            );
            Assert.Contains(snapshot.ClientTargets, target =>
                target.TargetId == "proc|persisted" && target.DisplayName == "Persisted Window"
            );
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    private static ConfigFileAccessor CreateAccessor(string dataFolder)
    {
        string originalDataFolder = StaticData.DataFolder;
        StaticData.DataFolder = dataFolder;
        try
        {
            ConstructorInfo? ctor = typeof(ConfigFileAccessor).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            );
            Assert.NotNull(ctor);
            return (ConfigFileAccessor)ctor.Invoke(null);
        }
        finally
        {
            StaticData.DataFolder = originalDataFolder;
        }
    }

    private static string CreateTempDataFolder()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "WindowSwitcher.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteDirectory(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        Directory.Delete(folder, recursive: true);
    }
}
