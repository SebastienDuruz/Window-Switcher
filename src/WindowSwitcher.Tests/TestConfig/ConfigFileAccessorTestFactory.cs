using WindowSwitcher.Lib.Data;

namespace WindowSwitcher.Tests.TestConfig;

/// <summary>
/// Creates <see cref="ConfigFileAccessor"/> instances bound to a temporary data folder instead of
/// the user's configuration. Tests using it belong to the
/// <see cref="ConfigFileAccessorIsolationCollection"/> because <see cref="StaticData.DataFolder"/>
/// is process-wide.
/// </summary>
internal static class ConfigFileAccessorTestFactory
{
    public static ConfigFileAccessor CreateAccessor(string dataFolder)
    {
        string originalDataFolder = StaticData.DataFolder;
        StaticData.DataFolder = dataFolder;
        try
        {
            return new ConfigFileAccessor();
        }
        finally
        {
            StaticData.DataFolder = originalDataFolder;
        }
    }

    public static string CreateTempDataFolder()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "WindowSwitcher.Tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void DeleteDirectory(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        Directory.Delete(folder, recursive: true);
    }
}
