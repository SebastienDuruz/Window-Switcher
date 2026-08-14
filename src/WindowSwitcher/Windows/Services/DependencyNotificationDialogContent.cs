using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowSwitcher.Windows.Services;

internal static class DependencyNotificationDialogContent
{
    public static string CreateTitle(int dependencyCount)
    {
        return dependencyCount == 1 ? "Missing dependency" : "Missing dependencies";
    }

    public static string CreateMessage(IReadOnlyCollection<string> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        string[] orderedDependencies = dependencies
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(dependency => dependency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedDependencies.Length == 0)
            throw new ArgumentException(
                "At least one dependency is required.",
                nameof(dependencies)
            );

        string label = orderedDependencies.Length == 1 ? "dependency" : "dependencies";
        string installInstruction =
            orderedDependencies.Length == 1
                ? "Install it and restart the app."
                : "Install them and restart the app.";

        return $"Missing {label}:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", orderedDependencies)}{Environment.NewLine}{Environment.NewLine}{installInstruction}";
    }
}
