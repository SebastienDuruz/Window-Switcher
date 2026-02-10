using System.Runtime.InteropServices;

namespace WindowSwitcherLib.Data.Commands;

public static class LinuxSessionDetector
{
    private static readonly LoginctlWrapper Loginctl = new();

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
            string fromSession = Normalize(Loginctl.Execute(
                ["show-session", sessionId.Trim(), "--property=Type", "--value"],
                timeoutMs: 2_000));
            if (!string.IsNullOrWhiteSpace(fromSession))
                return fromSession;
        }

        string fromSelf = Normalize(Loginctl.Execute(["show-session", "self", "--property=Type", "--value"], timeoutMs: 2_000));
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
}
