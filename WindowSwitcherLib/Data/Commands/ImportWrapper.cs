using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcherLib.Data.Commands;

public class ImportWrapper() : CommandBase("import"), ICommandWrapper
{
    public MemoryStream? CaptureScreenshotStream(string client)
    {
        if (!LinuxDependencies.IsImportAvailable)
        {
            LinuxDependencies.ReportMissingOnce("import");
            return null;
        }

        using var process = CreateProcess();
        int quality = ConfigFileAccessor.GetInstance().ReadConfig(config => config.ScreenshotQuality);
        process.StartInfo.Arguments = $"-window {client} -quality {quality} jpg:-";
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        var outputStream = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(outputStream);
        process.WaitForExit();
        _ = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0 || outputStream.Length == 0)
        {
            outputStream.Dispose();
            return null;
        }

        outputStream.Position = 0;
        return outputStream;
    }

    public string Execute(string client)
    {
        using var stream = CaptureScreenshotStream(client);
        return stream is null ? "error" : string.Empty;
    }
}
