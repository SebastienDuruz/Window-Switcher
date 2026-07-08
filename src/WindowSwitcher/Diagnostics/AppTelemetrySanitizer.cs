using System;

namespace WindowSwitcher.Diagnostics;

internal static class AppTelemetrySanitizer
{
    public static Exception CreateUnhandledException(object? exceptionObject)
    {
        return exceptionObject as Exception
            ?? new InvalidOperationException(
                $"Unhandled exception payload was not an Exception instance: {exceptionObject?.GetType().FullName ?? "null"}"
            );
    }

    public static string NormalizePreviewMode(string? previewMode)
    {
        if (string.IsNullOrWhiteSpace(previewMode))
            return "unknown";

        string normalized = previewMode.Trim().ToLowerInvariant();
        if (normalized.Contains("pipewire", StringComparison.Ordinal))
            return "pipewire";
        if (normalized.Contains("screenshot", StringComparison.Ordinal))
            return "screenshots";
        if (
            normalized.Contains("desktop window manager", StringComparison.Ordinal)
            || normalized.Contains("(dwm)", StringComparison.Ordinal)
            || string.Equals(normalized, "dwm", StringComparison.Ordinal)
        )
            return "dwm";

        return "other";
    }
}
