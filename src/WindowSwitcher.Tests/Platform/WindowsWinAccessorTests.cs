using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class WindowsWinAccessorTests
{
    [Fact]
    public async Task WindowOperations_ReturnFalse_ForInvalidWindowId()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var sut = new WindowsWinAccessor();

        Assert.False(await sut.TryActivateWindowAsync("invalid"));
        Assert.False(await sut.TryRenameWindowAsync("invalid", "New title"));
    }
}
