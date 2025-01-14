using System.ComponentModel;
using Newtonsoft.Json;
using WindowSwitcherLib.Models;

namespace WindowSwitcherLib.WindowAccess;

public class ConfigFileAccessor
{
    private static ConfigFileAccessor Instance { get; set; }
    private string FilePath { get; set; }
    public ConfigFile? Config { get; set; }
    
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

    public void ReadUserSettings()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                this.Config = JsonConvert.DeserializeObject<ConfigFile>(File.ReadAllText(FilePath));
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

    public void WriteUserSettings()
    {
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(this.Config, Formatting.Indented));
    }
}