using WindowSwitcher.Lib.Data.Platform.Policies;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PlatformPoliciesTests
{
    [Fact]
    public void LinuxAndWindowsSettingsPolicies_ExposeExpectedDecorationFlags()
    {
        Assert.True(new LinuxSettingsPlatformPolicy().ShowWindowDecorationSetting);
        Assert.False(new WindowsSettingsPlatformPolicy().ShowWindowDecorationSetting);
    }

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
}
