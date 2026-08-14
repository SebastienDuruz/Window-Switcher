using Microsoft.Extensions.DependencyInjection;
using WindowSwitcher.Hosting;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.SystemInfo.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Hosting;

public sealed class PlatformServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddPlatformServices_RegistersSharedPlatformSingletons()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices();
        await using ServiceProvider provider = services.BuildServiceProvider();

        WinAccessorBase firstAccessor = provider.GetRequiredService<WinAccessorBase>();
        WinAccessorBase secondAccessor = provider.GetRequiredService<WinAccessorBase>();
        IPreviewFrameProvider firstPreview =
            provider.GetRequiredService<IPreviewFrameProvider>();
        IPreviewFrameProvider secondPreview =
            provider.GetRequiredService<IPreviewFrameProvider>();
        IWindowKeybindActivator firstActivator =
            provider.GetRequiredService<IWindowKeybindActivator>();
        IWindowKeybindActivator secondActivator =
            provider.GetRequiredService<IWindowKeybindActivator>();

        Assert.Same(firstAccessor, secondAccessor);
        Assert.Same(firstPreview, secondPreview);
        Assert.Same(firstActivator, secondActivator);
    }

    [Fact]
    public async Task PlatformInfo_UsesTheProviderSelectionSnapshot()
    {
        var services = new ServiceCollection();
        services.AddPlatformServices();
        await using ServiceProvider provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IPreviewFrameProvider>();
        var appInfo = provider.GetRequiredService<IPlatformAppInfoProvider>().GetSnapshot();

        Assert.False(string.IsNullOrWhiteSpace(appInfo.PreviewMode));
        if (OperatingSystem.IsLinux())
        {
            Assert.Contains("session:", appInfo.DependencyStatus);
            Assert.Contains("window-control:", appInfo.DependencyStatus);
            Assert.Contains("preview:", appInfo.DependencyStatus);
        }
    }
}
