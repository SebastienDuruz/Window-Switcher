using System.Diagnostics;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcherLib.Data.Commands;

public sealed class GstLaunchWrapper() : CommandBase("gst-launch-1.0")
{
    public Process? StartPipelineToStdout(string pipeline)
    {
        if (!LinuxDependencies.IsGstLaunchAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gst-launch-1.0");
            return null;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gst-launch-1.0",
                Arguments = $"-q {pipeline}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            return process;
        }
        catch (Exception ex)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"gst-launch-1.0 start failed: {ex.Message}", StaticData.LogSeverity.WARN);
            process.Dispose();
            return null;
        }
    }
}

