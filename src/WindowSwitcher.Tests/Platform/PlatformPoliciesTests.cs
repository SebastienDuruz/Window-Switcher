using WindowSwitcher.Lib.Data.Platform.Policies;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PlatformPoliciesTests
{
    [Fact]
    public void LinuxAndWindowsPreviewPolicies_ExposeExpectedPreviewFlags()
    {
        var linuxPolicy = new LinuxFloatingPreviewPolicy();
        Assert.False(linuxPolicy.UseNativeThumbnailPreview);
        Assert.True(linuxPolicy.ShowScreenshotControl);
        Assert.True(linuxPolicy.RefreshScreenshotWhenDeselected);

        var windowsPolicy = new WindowsFloatingPreviewPolicy();
        Assert.True(windowsPolicy.UseNativeThumbnailPreview);
        Assert.False(windowsPolicy.ShowScreenshotControl);
        Assert.False(windowsPolicy.RefreshScreenshotWhenDeselected);
    }

    [Fact]
    public void NoOpNativeThumbnailRenderer_DoesNotRegisterThumbnail()
    {
        var renderer = new NoOpNativeThumbnailRenderer();

        bool registered = renderer.TryRegister(
            destinationWindowHandle: 1,
            sourceWindowHandle: 2,
            destinationBounds: new NativeThumbnailBounds(0, 0, 100, 100),
            out nint thumbnailHandle
        );

        Assert.False(registered);
        Assert.Equal(0, thumbnailHandle);
    }
}
