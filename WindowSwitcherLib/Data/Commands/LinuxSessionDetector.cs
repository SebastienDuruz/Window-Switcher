using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.Commands;

public static class LinuxSessionDetector
{
    public static string? GetSessionType()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        string? environmentValue = Normalize(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"));
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        string? sessionId = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            string fromSession = Normalize(RunCommand(
                "loginctl",
                ["show-session", sessionId.Trim(), "--property=Type", "--value"],
                timeoutMs: 2_000));
            if (!string.IsNullOrWhiteSpace(fromSession))
                return fromSession;
        }

        string fromSelf = Normalize(RunCommand("loginctl", ["show-session", "self", "--property=Type", "--value"], timeoutMs: 2_000));
        if (!string.IsNullOrWhiteSpace(fromSelf))
            return fromSelf;

        var shell = new ShWrapper();
        return Normalize(shell.Execute("echo $XDG_SESSION_TYPE"));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant();
    }

    private static string RunCommand(string fileName, IReadOnlyList<string> arguments, int timeoutMs)
    {
        using Process process = new();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.Arguments = string.Empty;
        process.StartInfo.ArgumentList.Clear();

        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
