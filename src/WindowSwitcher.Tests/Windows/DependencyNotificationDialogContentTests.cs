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
        string message = DependencyNotificationDialogContent.CreateMessage(
            ["pw-dump", "gst-launch-1.0", "PW-DUMP"]
        );

        Assert.Equal(
            """
            Missing dependencies:
            - gst-launch-1.0
            - pw-dump

            Install them and restart the app.
            """,
            message
        );
    }
}
