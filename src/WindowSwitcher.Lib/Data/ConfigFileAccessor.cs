using Newtonsoft.Json;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data;

    public class ConfigFileAccessor
{
    private static readonly Lazy<ConfigFileAccessor> Instance = new(() => new ConfigFileAccessor());
    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private ConfigFile _config;

    private ConfigFileAccessor()
    {
        StaticData.CheckFolders();
        _filePath = Path.Combine(StaticData.DataFolder, "config.json");
        _config = new ConfigFile();
        ReadUserSettings();
    }

    public static ConfigFileAccessor GetInstance()
    {
        return Instance.Value;
    }

    public string GetFilePath()
    {
        return _filePath;
    }

    private void ReadUserSettings()
    {
        lock (_syncRoot)
        {
            bool shouldPersistConfig = false;
            if (File.Exists(_filePath))
            {
                try
                {
                    string fileContents = File.ReadAllText(_filePath);
                    _config =
                        JsonConvert.DeserializeObject<ConfigFile>(fileContents) ?? new ConfigFile();
                }
                catch (Exception)
                {
                    _config = new ConfigFile();
                    WriteUserSettingsLocked();
                }
            }
            else
            {
                _config = new ConfigFile();
                WriteUserSettingsLocked();
            }

            _config.WhitelistPrefixes ??= new List<string>();
            _config.BlacklistPrefixes ??= new List<string>();
            _config.SentryDsn ??= string.Empty;
            if (!Guid.TryParse(_config.TelemetryUserId, out _))
            {
                _config.TelemetryUserId = Guid.NewGuid().ToString("D");
                shouldPersistConfig = true;
            }

            _config.FloatingWindowsConfig ??= new List<WindowConfig?>();
            _config.WindowKeybindTargets = KeybindCatalogBuilder
                .Build(_config.WindowKeybindTargets)
                .Targets.Select(KeybindCatalogBuilder.CloneTarget)
                .ToList();
            _config.FloatingWindowsConfig = _config
                .FloatingWindowsConfig.Where(x => x != null)
                .ToList();
            foreach (
                WindowConfig windowConfig in _config
                    .FloatingWindowsConfig.Where(x => x != null)
                    .Select(x => x!)
            )
            {
                if (string.IsNullOrWhiteSpace(windowConfig.ConfigKey))
                    windowConfig.ConfigKey = CreateConfigKey(windowConfig);
            }

            if (shouldPersistConfig)
                WriteUserSettingsLocked();
        }
    }

    public void WriteUserSettings()
    {
        lock (_syncRoot)
        {
            WriteUserSettingsLocked();
        }
    }

    public ConfigFile Config
    {
        get
        {
            lock (_syncRoot)
            {
                return _config;
            }
        }
    }

    public void SaveFloatingWindowSettings(WindowConfig windowConfig)
    {
        string configKey = CreateConfigKey(windowConfig);
        UpdateConfig(config =>
        {
            WindowConfig persistedConfig = windowConfig.Clone();
            persistedConfig.WindowId = string.Empty;
            persistedConfig.ConfigKey = configKey;
            config.FloatingWindowsConfig.RemoveAll(x => x != null && x.ConfigKey == configKey);
            config.FloatingWindowsConfig.Add(persistedConfig);
        });
    }

    public void ResetUserSettings()
    {
        _config = new ConfigFile();
        WriteUserSettings();
    }

    public void ResetFloatingWindowSettings()
    {
        _config.FloatingWindowsConfig.Clear();
        WriteUserSettings();
    }

    public void SavePrefixesList(List<string> prefixes)
    {
        UpdateConfig(config => config.WhitelistPrefixes = prefixes.ToList());
    }

    public void SaveBlacklist(List<string> blacklist)
    {
        UpdateConfig(config => config.BlacklistPrefixes = blacklist.ToList());
    }

    public void SaveWindowKeybindTargets(IReadOnlyCollection<WindowKeybindTargetConfig> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        UpdateConfig(config =>
            config.WindowKeybindTargets = KeybindCatalogBuilder
                .Build(targets)
                .Targets.Select(KeybindCatalogBuilder.CloneTarget)
                .ToList()
        );
    }

    public WindowConfig? GetFloatingWindowConfig(WindowConfig windowConfig)
    {
        lock (_syncRoot)
        {
            string configKey = CreateConfigKey(windowConfig);
            WindowConfig? existingConfig = _config.FloatingWindowsConfig.FirstOrDefault(x =>
                x != null && x.ConfigKey == configKey
            );

            if (existingConfig == null)
            {
                string normalizedTitle = NormalizeKeyPart(windowConfig.WindowTitle);
                existingConfig = _config.FloatingWindowsConfig.FirstOrDefault(x =>
                    x != null && NormalizeKeyPart(x.WindowTitle) == normalizedTitle
                );
                if (existingConfig != null && existingConfig.ConfigKey != configKey)
                    existingConfig.ConfigKey = configKey;
            }

            if (existingConfig == null)
                return null;

            WindowConfig persistedConfig = existingConfig.Clone();
            persistedConfig.WindowId = windowConfig.WindowId;
            persistedConfig.ProcessName = windowConfig.ProcessName;
            persistedConfig.ConfigKey = configKey;
            return persistedConfig;
        }
    }

    public T ReadConfig<T>(Func<ConfigFile, T> reader)
    {
        lock (_syncRoot)
        {
            return reader(_config);
        }
    }

    public void UpdateConfig(Action<ConfigFile> update)
    {
        lock (_syncRoot)
        {
            update(_config);
        }
    }

    private void WriteUserSettingsLocked()
    {
        File.WriteAllText(_filePath, JsonConvert.SerializeObject(_config, Formatting.Indented));
    }

    private static string CreateConfigKey(WindowConfig windowConfig)
    {
        string title = NormalizeKeyPart(windowConfig.WindowTitle);
        string processName = NormalizeKeyPart(windowConfig.ProcessName);
        if (string.IsNullOrWhiteSpace(processName))
            return title;
        return $"{processName}|{title}";
    }

    private static string NormalizeKeyPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}
