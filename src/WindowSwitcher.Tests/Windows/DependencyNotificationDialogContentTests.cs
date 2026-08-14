using WindowSwitcher.Windows.Services;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class DependencyNotificationDialogContentTests
{
    [Fact]
    public void CreateTitle_ReturnsSingularOrPluralTitle()
    {
        Assert.Equal("Missing dependency", DependencyNotificationDialogContent.CreateTitle(1));
        Assert.Equal("Missing dependencies", DependencyNotificationDialogContent.CreateTitle(2));
    }

    [Fact]
    public void CreateMessage_ReturnsSortedDistinctDependencyList()
    {
        string message = DependencyNotificationDialogContent.CreateMessage([
            "libpipewire-0.3.so.0",
            "xdg-desktop-portal",
            "LIBPIPEWIRE-0.3.SO.0",
        ]);

        Assert.Equal(
            """
            Missing dependencies:
            - libpipewire-0.3.so.0
            - xdg-desktop-portal

            Install them and restart the app.
            """,
            message
        );
    }
}
