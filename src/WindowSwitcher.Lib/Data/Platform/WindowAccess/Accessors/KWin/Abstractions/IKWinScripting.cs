using Tmds.DBus;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.KWin.Abstractions;

// Tmds.DBus derives D-Bus member names from method names, so these keep KWin's camelCase names.
#pragma warning disable IDE1006

/// <summary>Defines the D-Bus contract of the KWin script loader.</summary>
[DBusInterface("org.kde.kwin.Scripting")]
public interface IKWinScripting : IDBusObject
{
    /// <summary>Loads a JavaScript file as a KWin script.</summary>
    /// <param name="filePath">Absolute path of the script file.</param>
    /// <param name="pluginName">Unique name identifying the loaded script.</param>
    /// <returns>The script identifier, or a negative value when loading failed.</returns>
    Task<int> loadScriptAsync(string filePath, string pluginName);

    /// <summary>Unloads a KWin script.</summary>
    /// <param name="pluginName">Name given when the script was loaded.</param>
    /// <returns><see langword="true" /> when a script was unloaded.</returns>
    Task<bool> unloadScriptAsync(string pluginName);

    /// <summary>Checks whether a KWin script is loaded.</summary>
    /// <param name="pluginName">Name given when the script was loaded.</param>
    /// <returns><see langword="true" /> when the script is loaded.</returns>
    Task<bool> isScriptLoadedAsync(string pluginName);
}

/// <summary>Defines the D-Bus contract of a loaded KWin script.</summary>
[DBusInterface("org.kde.kwin.Script")]
public interface IKWinScript : IDBusObject
{
    /// <summary>Starts the script.</summary>
    Task runAsync();
}

#pragma warning restore IDE1006
