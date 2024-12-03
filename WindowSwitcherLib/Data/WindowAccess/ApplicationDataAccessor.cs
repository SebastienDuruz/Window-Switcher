namespace WindowSwitcherLib.WindowAccess;

public class ApplicationDataAccessor
{
    public static string ScreenshotFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowSwitcher");

    public ApplicationDataAccessor()
    {
        this.CheckFolder();
    }

    private void CheckFolder()
    {
        if(!Directory.Exists(ScreenshotFolder))
            Directory.CreateDirectory(ScreenshotFolder);
    }
}