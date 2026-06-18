using System.Collections.Generic;

namespace WindowSwitcher.ViewModels.Abstractions;

public interface IWindowFilterSettingsProvider
{
    WindowFilterSettings GetSettings();
}

public sealed record WindowFilterSettings(
    IReadOnlySet<string> BlacklistPrefixes,
    IReadOnlyCollection<string> WhitelistPrefixes
);
