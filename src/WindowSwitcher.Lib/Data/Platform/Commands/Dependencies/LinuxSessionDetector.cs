using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Wrappers;

namespace WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

public static class LinuxSessionDetector
{
    private static readonly LoginctlWrapper Loginctl = new();

    public static string? GetSessionType()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        string? environmentValue = Normalize(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")
        );
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        return null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant();
    }
}
