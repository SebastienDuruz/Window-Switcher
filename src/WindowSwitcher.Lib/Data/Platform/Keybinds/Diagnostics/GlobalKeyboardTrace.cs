using System.Diagnostics;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Diagnostics;

internal static class GlobalKeyboardTrace
{
    private const string Prefix = "[GlobalKeyboard]";

    public static void Debug(string message)
    {
        Trace.WriteLine($"{Prefix} {message}");
    }

    public static void Info(string message)
    {
        Trace.TraceInformation($"{Prefix} {message}");
    }

    public static void Warning(string message)
    {
        Trace.TraceWarning($"{Prefix} {message}");
    }

    public static void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Trace.TraceError($"{Prefix} {message}");
            return;
        }

        Trace.TraceError($"{Prefix} {message}{Environment.NewLine}{exception}");
    }
}
