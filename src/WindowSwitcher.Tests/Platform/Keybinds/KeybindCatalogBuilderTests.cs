using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Keybinds;

public sealed class KeybindCatalogBuilderTests
{
    [Fact]
    public void Build_NormalizesTargetsAndFiltersInvalidBindings()
    {
        KeybindCatalog catalog = KeybindCatalogBuilder.Build([
            new WindowKeybindTargetConfig
            {
                TargetId = "  Proc|Editor  ",
                DisplayLabel = "  Editor Window  ",
                Shortcuts =
                [
                    new WindowKeybindShortcut
                    {
                        Combination = new KeyCombination { Ctrl = true, Key = KeybindPrimaryKey.A },
                    },
                    new WindowKeybindShortcut { Combination = new KeyCombination() },
                ],
            },
            new WindowKeybindTargetConfig
            {
                TargetId = "",
                DisplayLabel = "invalid",
                Shortcuts = [],
            },
        ]);

        WindowKeybindTargetConfig target = Assert.Single(catalog.Targets);
        Assert.Equal("proc|editor", target.TargetId);
        Assert.Equal("Editor Window", target.DisplayLabel);

        WindowKeybindShortcut binding = Assert.Single(target.Shortcuts);
        Assert.True(binding.Combination.Ctrl);
        Assert.Equal(KeybindPrimaryKey.A, binding.Combination.Key);
    }

    [Fact]
    public void Build_ResolvesUsingFirstMatchingTarget()
    {
        var combination = new KeyCombination { Alt = true, Key = KeybindPrimaryKey.F2 };

        KeybindCatalog catalog = KeybindCatalogBuilder.Build([
            new WindowKeybindTargetConfig
            {
                TargetId = "proc|editor",
                DisplayLabel = "Editor",
                Shortcuts = [new WindowKeybindShortcut { Combination = combination }],
            },
            new WindowKeybindTargetConfig
            {
                TargetId = "proc|terminal",
                DisplayLabel = "Terminal",
                Shortcuts = [new WindowKeybindShortcut { Combination = combination }],
            },
        ]);

        Assert.True(catalog.TryResolveTarget(combination, out string targetId));
        Assert.Equal("proc|editor", targetId);
    }
}
