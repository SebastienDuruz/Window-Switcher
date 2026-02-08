using System.Diagnostics;
using WindowSwitcherLib.Data.FileAccess;

namespace WindowSwitcherLib.Data.Commands;

public sealed class GdbusWrapper() : CommandBase("gdbus")
{
    public string? CallSession(string dest, string objectPath, string method, string args)
    {
        if (!LinuxDependencies.IsGdbusAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gdbus");
            return null;
        }

        using Process process = CreateProcess();
        process.StartInfo.Arguments = $"call --session -d {dest} -o {objectPath} -m {method} {args}";
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            if (ConfigFileAccessor.GetInstance().ReadConfig(config => config.ActivateLogs))
                AppLogger.Log($"gdbus call failed ({process.ExitCode}): {stderr}", StaticData.LogSeverity.WARN);
            return null;
        }

        return string.IsNullOrWhiteSpace(stdout) ? null : stdout.Trim();
    }

    public Process? StartMonitorSession(string dest, string objectPath)
    {
        if (!LinuxDependencies.IsGdbusAvailable)
        {
            LinuxDependencies.ReportMissingOnce("gdbus");
            return null;
        }

        Process process = CreateProcess();
        process.StartInfo.Arguments = $"monitor --session -d {dest} -o {objectPath}";
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        return process;
    }
}

