using Newtonsoft.Json;
using WindowSwitcherLib.Models;
using WindowSwitcherLib.WindowAccess;

namespace WindowSwitcherLib.Data.FileAccess;

public class ConfigFileAccessor
{
    private static ConfigFileAccessor Instance { get; set; }
    private string FilePath { get; set; }
    public ConfigFile Config { get; set; }
    
    private ConfigFileAccessor()
    {
        Directory.CreateDirectory(StaticData.DataFolder);
        Directory.CreateDirectory(StaticData.ScreenshotFolder);
        FilePath = Path.Combine(StaticData.DataFolder, "config.json");
        ReadUserSettings();
    }

    public static ConfigFileAccessor GetInstance()
    {
        if (Instance == null)
            Instance = new ConfigFileAccessor();
        return Instance;
    }
    
    public string GetFilePath()
    {
        return FilePath;
    }

    public void ReadUserSettings()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                Config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                // Reset the settings by recreating a file
                Config = new ConfigFile();
                WriteUserSettings();
            }
        }
        else
        {
            Config = new ConfigFile();
            WriteUserSettings();
        }
    }

    public void WriteUserSettings()
    {
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
    }

    public void SaveFloatingWindowSettings(WindowConfig windowConfig)
    {
        Config.FloatingWindowsConfig.RemoveAll(x => x.WindowTitle == windowConfig.WindowTitle);
        Config.FloatingWindowsConfig.Add(windowConfig);
    }

    public void SavePrefixesList(List<string> prefixes)
    {
        Config.WhitelistPrefixes = prefixes;
    }

    public void SaveBlacklist(List<string> blacklist)
    {
        Config.BlacklistPrefixes = blacklist;
    }

    public WindowConfig? GetFloatingWindowConfig(WindowConfig windowConfig)
    {
        // Check by windowTitle
        WindowConfig existantConfig =
            Config.FloatingWindowsConfig.FirstOrDefault(x => x.WindowTitle == windowConfig.WindowTitle);
        if (existantConfig != null)
        {
            existantConfig.WindowId = windowConfig.WindowId;
            SaveFloatingWindowSettings(existantConfig);
            return existantConfig;
        }

        return null;
    }
}