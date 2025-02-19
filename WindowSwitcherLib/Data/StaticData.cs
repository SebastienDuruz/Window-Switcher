using System.Runtime.InteropServices;
using Avalonia;
using Microsoft.VisualBasic.FileIO;

namespace WindowSwitcherLib.WindowAccess;

public static class StaticData
{
    public enum PrefixWindowType
    {
        whitelist,
        blacklist
    }

    public enum LogSeverity
    {
        INFO,
        WARN,
        ERRO,
        CRIT
    }

    /// <summary>
    /// Used to give the information to floating windows that the mainWindow is closing
    /// </summary>
    public static bool AppClosing { get; set; } = false;

    public static string AppName { get; set; } = "WindowSwitcher";
}