using System.ComponentModel;
using Newtonsoft.Json;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class ConfigFileAccessor
{
    private static ConfigFileAccessor Instance { get; set; }
    private string FilePath { get; set; }
    public ConfigFile Config { get; set; }
    
    private ConfigFileAccessor()
    {
        Directory.CreateDirectory(StaticData.DataFolder);
        this.FilePath = Path.Combine(StaticData.DataFolder, "config.json");
        this.ReadUserSettings();
    }

    public static ConfigFileAccessor GetInstance()
    {
        if (Instance == null)
            Instance = new ConfigFileAccessor();
        return Instance;
    }
    
    public string GetFilePath()
    {
        return this.FilePath;
    }

    public async void ReadUserSettings()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                this.Config = JsonConvert.DeserializeObject<ConfigFile>(await File.ReadAllTextAsync(FilePath));
            }
            catch (Exception ex)
            {
                // Reset the settings by recreating a file
                this.Config = new ConfigFile();
                WriteUserSettings();
            }
        }
        else
        {
            this.Config = new ConfigFile();
            WriteUserSettings();
        }
    }

    public async void WriteUserSettings()
    {
        await File.WriteAllTextAsync(FilePath, JsonConvert.SerializeObject(this.Config, Formatting.Indented));
    }
}