using System.Reflection;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

[Collection(ConfigFileAccessorIsolationCollection.Name)]
public sealed class WindowKeybindManagerTests
{
    [Fact]
    public void TryAddBinding_DetectsDuplicateAndConflict()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            ConfigFileAccessor accessor = CreateAccessor(dataFolder);
            var sut = new WindowKeybindManager(accessor);

            var ctrlA = new KeyCombination { Ctrl = true, Key = KeybindPrimaryKey.A };
            KeybindShortcutAddResult firstResult = sut.AddShortcut(
                "proc|editor",
                "Editor",
                ctrlA
            );
            KeybindShortcutAddResult duplicateResult = sut.AddShortcut(
                "proc|editor",
                "Editor",
                ctrlA
            );
            KeybindShortcutAddResult conflictResult = sut.AddShortcut(
                "proc|terminal",
                "Terminal",
                ctrlA
            );

            Assert.Equal(KeybindRegistrationResult.Added, firstResult.Status);
            Assert.Equal(string.Empty, firstResult.Message);
            Assert.Equal(KeybindRegistrationResult.Duplicate, duplicateResult.Status);
            Assert.Contains("already assigned", duplicateResult.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(KeybindRegistrationResult.Conflict, conflictResult.Status);
            Assert.Contains("conflict", conflictResult.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void TryResolveTarget_ReturnsTargetForMatchingCombination()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            ConfigFileAccessor accessor = CreateAccessor(dataFolder);
            var sut = new WindowKeybindManager(accessor);

            var combination = new KeyCombination
            {
                Alt = true,
                Shift = true,
                Key = KeybindPrimaryKey.F2,
            };

            KeybindShortcutAddResult added = sut.AddShortcut(
                "proc|editor",
                "Editor",
                combination
            );
            bool resolved = sut.TryResolveTarget(combination, out string targetId);

            Assert.Equal(KeybindRegistrationResult.Added, added.Status);
            Assert.True(resolved);
            Assert.Equal("proc|editor", targetId);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void RemoveBinding_RemovesBindingAndEmptyTarget()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            ConfigFileAccessor accessor = CreateAccessor(dataFolder);
            var sut = new WindowKeybindManager(accessor);
            var combination = new KeyCombination { Key = KeybindPrimaryKey.F1 };

            _ = sut.AddShortcut("proc|editor", "Editor", combination);
            bool removed = sut.RemoveShortcut("proc|editor", combination);
            bool resolvedAfterRemoval = sut.TryResolveTarget(combination, out _);

            Assert.True(removed);
            Assert.False(resolvedAfterRemoval);
            Assert.Empty(sut.GetTargets());
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
