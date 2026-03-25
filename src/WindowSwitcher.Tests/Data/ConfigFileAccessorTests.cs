using System.Reflection;
using WindowSwitcher.Lib.Data;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Data;

[Collection(ConfigFileAccessorIsolationCollection.Name)]
public sealed class ConfigFileAccessorTests
{
    [Fact]
    public void Constructor_CreatesConfigFile_WhenMissing()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);

            Assert.Equal(Path.Combine(dataFolder, "config.json"), sut.GetFilePath());
            Assert.True(File.Exists(sut.GetFilePath()));
            Assert.NotNull(sut.Config.WhitelistPrefixes);
            Assert.NotNull(sut.Config.BlacklistPrefixes);
            Assert.NotNull(sut.Config.FloatingWindowsConfig);
            Assert.NotNull(sut.Config.WindowKeybindTargets);
            Assert.True(sut.Config.EnableSentry);
            Assert.Equal(new ConfigFile().SentryDsn, sut.Config.SentryDsn);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void ReadUserSettings_NormalizesCollectionsAndRestoreMaps()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            const string persistedJson = """
                {
                  "WhitelistPrefixes": null,
                  "BlacklistPrefixes": null,
                  "SentryDsn": null,
                  "LinuxWaylandScreenCastRestoreToken": null,
                  "LinuxWaylandScreenCastRestoreTokensByWindowId": {
                    "  win-1  ": "  token-1  ",
                    "": "ignored",
                    "win-2": "   "
                  },
                  "LinuxWaylandScreenCastRestoreDataByWindowId": {
                    "  win-2  ": "  restore-data  ",
                    "  ": "ignored"
                  },
                  "LinuxWaylandScreenCastStreamIdsByWindowId": {
                    "  win-3  ": "  stream-3  ",
                    "win-4": ""
                  },
                  "FloatingWindowsConfig": [
                    null,
                    {
                      "WindowId": "persisted-id",
                      "ProcessName": "  Proc  ",
                      "WindowTitle": "  My Title  ",
                      "ConfigKey": ""
                    }
                  ],
                  "WindowKeybindTargets": [
                    {
                      "TargetId": "  Proc|Editor  ",
                      "DisplayLabel": "  Editor Window  ",
                      "Shortcuts": [
                        {
                          "Combination": {
                            "Ctrl": true,
                            "Alt": false,
                            "Shift": false,
                            "Meta": false,
                            "Key": "A"
                          }
                        },
                        {
                          "Combination": {
                            "Key": "None"
                          }
                        }
                      ]
                    },
                    {
                      "TargetId": "",
                      "DisplayLabel": "invalid",
                      "Shortcuts": []
                    }
                  ]
                }
                """;
            File.WriteAllText(Path.Combine(dataFolder, "config.json"), persistedJson);

            var sut = CreateAccessor(dataFolder);
            ConfigFile config = sut.Config;

            Assert.Empty(config.WhitelistPrefixes);
            Assert.Empty(config.BlacklistPrefixes);
            Assert.True(config.EnableSentry);
            Assert.Equal(string.Empty, config.SentryDsn);
            Assert.Equal(string.Empty, config.LinuxWaylandScreenCastRestoreToken);

            Assert.Single(config.LinuxWaylandScreenCastRestoreTokensByWindowId);
            Assert.Equal(
                "token-1",
                config.LinuxWaylandScreenCastRestoreTokensByWindowId["win-1"]
            );

            Assert.Single(config.LinuxWaylandScreenCastRestoreDataByWindowId);
            Assert.Equal(
                "restore-data",
                config.LinuxWaylandScreenCastRestoreDataByWindowId["win-2"]
            );

            Assert.Single(config.LinuxWaylandScreenCastStreamIdsByWindowId);
            Assert.Equal(
                "stream-3",
                config.LinuxWaylandScreenCastStreamIdsByWindowId["win-3"]
            );

            WindowConfig persistedConfig = Assert.Single(
                config.FloatingWindowsConfig.Where(entry => entry is not null).Select(entry => entry!)
            );
            Assert.Equal("proc|my title", persistedConfig.ConfigKey);

            WindowKeybindTargetConfig keybindTarget = Assert.Single(config.WindowKeybindTargets);
            Assert.Equal("proc|editor", keybindTarget.TargetId);
            Assert.Equal("Editor Window", keybindTarget.DisplayLabel);
            WindowKeybindShortcut shortcut = Assert.Single(keybindTarget.Shortcuts);
            Assert.True(shortcut.Combination.Ctrl);
            Assert.Equal(KeybindPrimaryKey.A, shortcut.Combination.Key);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void SaveFloatingWindowSettings_ReplacesEntryWithSameConfigKey_AndClearsWindowId()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);
            var first = new WindowConfig
            {
                WindowId = "window-1",
                ProcessName = "Proc",
                WindowTitle = "Editor",
                WindowWidth = 400,
                WindowHeight = 220,
            };
            var second = new WindowConfig
            {
                WindowId = "window-2",
                ProcessName = "proc",
                WindowTitle = " editor ",
                WindowWidth = 640,
                WindowHeight = 320,
            };

            sut.SaveFloatingWindowSettings(first);
            sut.SaveFloatingWindowSettings(second);

            WindowConfig persisted = Assert.Single(
                sut.ReadConfig(config =>
                    config.FloatingWindowsConfig.Where(entry => entry is not null)
                        .Select(entry => entry!)
                        .ToList()
                )
            );
            Assert.Equal(string.Empty, persisted.WindowId);
            Assert.Equal("proc|editor", persisted.ConfigKey);
            Assert.Equal(640d, persisted.WindowWidth);
            Assert.Equal(320d, persisted.WindowHeight);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void GetFloatingWindowConfig_UsesTitleFallbackAndReturnsDetachedClone()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);
            sut.SaveFloatingWindowSettings(
                new WindowConfig
                {
                    WindowId = "persisted",
                    ProcessName = string.Empty,
                    WindowTitle = "  Editor  ",
                    WindowWidth = 420,
                    WindowHeight = 240,
                }
            );

            WindowConfig? resolved = sut.GetFloatingWindowConfig(
                new WindowConfig
                {
                    WindowId = "runtime-id",
                    ProcessName = "Code",
                    WindowTitle = "editor",
                }
            );

            Assert.NotNull(resolved);
            Assert.Equal("runtime-id", resolved!.WindowId);
            Assert.Equal("Code", resolved.ProcessName);
            Assert.Equal("code|editor", resolved.ConfigKey);
            Assert.Equal(420d, resolved.WindowWidth);
            Assert.Equal(240d, resolved.WindowHeight);

            resolved.WindowWidth = 999;
            WindowConfig persisted = Assert.Single(
                sut.ReadConfig(config =>
                    config.FloatingWindowsConfig.Where(entry => entry is not null)
                        .Select(entry => entry!)
                        .ToList()
                )
            );
            Assert.Equal(420d, persisted.WindowWidth);
            Assert.Equal("code|editor", persisted.ConfigKey);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void SavePrefixesAndBlacklist_CopyInputLists()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);
            var prefixes = new List<string> { "alpha" };
            var blacklist = new List<string> { "blocked" };

            sut.SavePrefixesList(prefixes);
            sut.SaveBlacklist(blacklist);

            prefixes.Add("beta");
            blacklist.Add("blocked-2");

            ConfigFile config = sut.Config;
            Assert.Equal(new[] { "alpha" }, config.WhitelistPrefixes);
            Assert.Equal(new[] { "blocked" }, config.BlacklistPrefixes);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void WriteUserSettings_PersistsUpdatedValuesToDisk()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);

            sut.UpdateConfig(config => config.WindowWidth = 777);
            sut.WriteUserSettings();

            string json = File.ReadAllText(sut.GetFilePath());
            Assert.Contains("\"WindowWidth\": 777", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void WriteUserSettings_PersistsEnableSentryValueToDisk()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);

            sut.UpdateConfig(config => config.EnableSentry = false);
            sut.WriteUserSettings();

            string json = File.ReadAllText(sut.GetFilePath());
            Assert.Contains("\"EnableSentry\": false", json, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void WriteUserSettings_PersistsSentryDsnValueToDisk()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);

            sut.UpdateConfig(config => config.SentryDsn = "https://examplePublicKey@o0.ingest.sentry.io/0");
            sut.WriteUserSettings();

            string json = File.ReadAllText(sut.GetFilePath());
            Assert.Contains(
                "\"SentryDsn\": \"https://examplePublicKey@o0.ingest.sentry.io/0\"",
                json,
                StringComparison.Ordinal
            );
        }
        finally
        {
            DeleteDirectory(dataFolder);
        }
    }

    [Fact]
    public void SaveWindowKeybindTargets_PersistsStructuredShortcuts()
    {
        string dataFolder = CreateTempDataFolder();
        try
        {
            var sut = CreateAccessor(dataFolder);
            sut.SaveWindowKeybindTargets(
                [
                    new WindowKeybindTargetConfig
                    {
                        TargetId = "proc|editor",
                        DisplayLabel = "Editor",
                        Shortcuts =
                        [
                            new WindowKeybindShortcut
                            {
                                Combination = new KeyCombination
                                {
                                    Alt = true,
                                    Shift = true,
                                    Key = KeybindPrimaryKey.F2,
                                },
                            },
                        ],
                    },
                ]
            );
            sut.WriteUserSettings();

            string json = File.ReadAllText(sut.GetFilePath());
            Assert.Contains("\"WindowKeybindTargets\"", json, StringComparison.Ordinal);
            Assert.Contains("\"TargetId\": \"proc|editor\"", json, StringComparison.Ordinal);
            Assert.Contains("\"Key\": \"F2\"", json, StringComparison.Ordinal);
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
