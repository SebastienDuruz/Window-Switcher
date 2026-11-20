using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Services;
using Xunit;
using static WindowSwitcher.Tests.TestConfig.ConfigFileAccessorTestFactory;

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
            KeybindShortcutAddResult firstResult = sut.AddShortcut("proc|editor", "Editor", ctrlA);
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
            Assert.Contains(
                "already assigned",
                duplicateResult.Message,
                StringComparison.OrdinalIgnoreCase
            );
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

            KeybindShortcutAddResult added = sut.AddShortcut("proc|editor", "Editor", combination);
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

    [Fact]
    public void GetShortcutsForTarget_ReturnsCopiesForNormalizedTargetId()
    {
        WithManager(sut =>
        {
            var combination = new KeyCombination { Ctrl = true, Key = KeybindPrimaryKey.E };
            _ = sut.AddShortcut("proc|editor", "Editor", combination);

            WindowKeybindShortcut shortcut = Assert.Single(
                sut.GetShortcutsForTarget("  PROC|Editor ")
            );
            shortcut.Combination.Key = KeybindPrimaryKey.Z;

            Assert.Equal(
                KeybindPrimaryKey.E,
                Assert.Single(sut.GetShortcutsForTarget("proc|editor")).Combination.Key
            );
        });
    }

    [Theory]
    [InlineData("proc|unknown")]
    [InlineData("   ")]
    public void GetShortcutsForTarget_ReturnsEmpty_ForUnknownOrBlankTarget(string targetId)
    {
        WithManager(sut =>
        {
            _ = sut.AddShortcut(
                "proc|editor",
                "Editor",
                new KeyCombination { Key = KeybindPrimaryKey.F1 }
            );

            Assert.Empty(sut.GetShortcutsForTarget(targetId));
        });
    }

    [Fact]
    public void UpsertTarget_CreatesTargetAndRaisesBindingsChanged()
    {
        WithManager(sut =>
        {
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            sut.UpsertTarget(" Proc|Editor ", " Editor ");

            WindowKeybindTargetConfig target = Assert.Single(sut.GetTargets());
            Assert.Equal("proc|editor", target.TargetId);
            Assert.Equal("Editor", target.DisplayLabel);
            Assert.Empty(target.Shortcuts);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void UpsertTarget_RenamesExistingTarget_OnlyWhenLabelChanges()
    {
        WithManager(sut =>
        {
            sut.UpsertTarget("proc|editor", "Editor");
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            sut.UpsertTarget("proc|editor", "Editor");
            sut.UpsertTarget("proc|editor", "   ");
            Assert.Equal(0, changes);
            Assert.Equal("Editor", Assert.Single(sut.GetTargets()).DisplayLabel);

            sut.UpsertTarget("proc|editor", "Code editor");
            Assert.Equal(1, changes);
            Assert.Equal("Code editor", Assert.Single(sut.GetTargets()).DisplayLabel);
        });
    }

    [Fact]
    public void UpsertTarget_IgnoresBlankTargetId()
    {
        WithManager(sut =>
        {
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            sut.UpsertTarget("  ", "Editor");

            Assert.Empty(sut.GetTargets());
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void AddShortcut_RejectsBlankTargetAndShortcutWithoutPrimaryKey()
    {
        WithManager(sut =>
        {
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            KeybindShortcutAddResult blankTarget = sut.AddShortcut(
                " ",
                "Editor",
                new KeyCombination { Key = KeybindPrimaryKey.F1 }
            );
            KeybindShortcutAddResult noPrimaryKey = sut.AddShortcut(
                "proc|editor",
                "Editor",
                new KeyCombination { Ctrl = true }
            );

            Assert.Equal(KeybindRegistrationResult.Invalid, blankTarget.Status);
            Assert.Equal(KeybindRegistrationResult.Invalid, noPrimaryKey.Status);
            Assert.Empty(sut.GetTargets());
            Assert.Equal(0, changes);
        });
    }

    [Fact]
    public void AddShortcut_RaisesBindingsChanged_OnlyWhenAShortcutIsAdded()
    {
        WithManager(sut =>
        {
            var combination = new KeyCombination { Alt = true, Key = KeybindPrimaryKey.D1 };
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            _ = sut.AddShortcut("proc|editor", "Editor", combination);
            _ = sut.AddShortcut("proc|editor", "Editor", combination);
            _ = sut.AddShortcut("proc|terminal", "Terminal", combination);

            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void AddShortcut_UsesTargetLabelInConflictMessage()
    {
        WithManager(sut =>
        {
            var combination = new KeyCombination { Ctrl = true, Key = KeybindPrimaryKey.T };
            _ = sut.AddShortcut("proc|terminal", "Terminal", combination);

            KeybindShortcutAddResult conflict = sut.AddShortcut(
                "proc|editor",
                "Editor",
                combination
            );

            Assert.Equal("Conflict: Ctrl+T is already assigned to \"Terminal\".", conflict.Message);
        });
    }

    [Fact]
    public void RemoveShortcut_KeepsTargetWhileOtherShortcutsRemain()
    {
        WithManager(sut =>
        {
            var first = new KeyCombination { Key = KeybindPrimaryKey.F1 };
            var second = new KeyCombination { Key = KeybindPrimaryKey.F2 };
            _ = sut.AddShortcut("proc|editor", "Editor", first);
            _ = sut.AddShortcut("proc|editor", "Editor", second);

            Assert.True(sut.RemoveShortcut("proc|editor", first));

            WindowKeybindTargetConfig target = Assert.Single(sut.GetTargets());
            Assert.Equal(KeybindPrimaryKey.F2, Assert.Single(target.Shortcuts).Combination.Key);
        });
    }

    [Fact]
    public void RemoveShortcut_ReturnsFalse_WithoutRaisingBindingsChanged_WhenNothingMatches()
    {
        WithManager(sut =>
        {
            _ = sut.AddShortcut(
                "proc|editor",
                "Editor",
                new KeyCombination { Key = KeybindPrimaryKey.F1 }
            );
            int changes = 0;
            sut.BindingsChanged += (_, _) => changes++;

            Assert.False(
                sut.RemoveShortcut(
                    "proc|unknown",
                    new KeyCombination { Key = KeybindPrimaryKey.F1 }
                )
            );
            Assert.False(
                sut.RemoveShortcut("proc|editor", new KeyCombination { Key = KeybindPrimaryKey.F9 })
            );
            Assert.False(sut.RemoveShortcut("proc|editor", new KeyCombination()));
            Assert.False(
                sut.RemoveShortcut(" ", new KeyCombination { Key = KeybindPrimaryKey.F1 })
            );

            Assert.Equal(0, changes);
            Assert.Single(sut.GetTargets());
        });
    }

    private static void WithManager(Action<WindowKeybindManager> test)
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            test(new WindowKeybindManager(CreateAccessor(dataFolder)));
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }
}
