using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;

namespace WindowSwitcher.Diagnostics;

/// <summary>
/// Non-identifying runtime facts shared by telemetry tags and the log file header.
/// </summary>
internal static class AppRuntimeInfo
{
    public static string GetInformationalVersion()
    {
        return typeof(App)
                .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? typeof(App).Assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    public static string GetBuildChannel()
    {
#if DEBUG
        return "debug";
#else
        return "production";
#endif
    }

    public static string GetOperatingSystemTag()
    {
        if (OperatingSystem.IsLinux())
            return "linux";
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";

        return "unknown";
    }

    public static string GetProcessArchitectureTag()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };
    }

    public static string GetSessionTypeTag()
    {
        if (!OperatingSystem.IsLinux())
            return "not_applicable";

        string? sessionType = LinuxSessionDetector.GetSessionType();
        return string.IsNullOrWhiteSpace(sessionType) ? "unknown" : NormalizeTagValue(sessionType);
    }

    public static string GetDistributionChannel()
    {
        return GetAssemblyMetadataValue("TelemetryDistributionChannel", "source");
    }

    public static string GetPackageKind()
    {
        return GetAssemblyMetadataValue("TelemetryPackageKind", "unpackaged");
    }

    public static string NormalizeTagValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string GetAssemblyMetadataValue(string key, string fallback)
    {
        string? value = typeof(App)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, key, StringComparison.Ordinal)
            )
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? fallback : NormalizeTagValue(value);
    }
}
