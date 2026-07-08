using WindowSwitcher.Lib.Data.Updates;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class StoreManagedAppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsStoreManagedMessage()
    {
        var sut = new StoreManagedAppUpdateService();

        UpdateCheckResult result = await sut.CheckForUpdatesAsync("0.12.0");

        Assert.False(result.IsUpdateAvailable);
        Assert.False(result.CanStartUpdate);
        Assert.Equal("0.12.0", result.CurrentVersion);
        Assert.Contains("Microsoft Store", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaunchUpdateAsync_ReturnsFailureWithoutClosingApplication()
    {
        var sut = new StoreManagedAppUpdateService();

        UpdateLaunchResult result = await sut.LaunchUpdateAsync(new UpdateCheckResult());

        Assert.False(result.Launched);
        Assert.False(result.ShouldCloseApplication);
        Assert.Contains("Microsoft Store", result.Message, StringComparison.Ordinal);
    }
}
