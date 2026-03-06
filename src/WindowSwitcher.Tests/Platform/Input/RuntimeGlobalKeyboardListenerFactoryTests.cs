using WindowSwitcher.Lib.Data.Platform.Keybinds.Factories;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

public sealed class RuntimeGlobalKeyboardListenerFactoryTests
{
    [Fact]
    public void Create_ReturnsListenerForCurrentPlatform()
    {
        var factory = new RuntimeGlobalKeyboardListenerFactory();

        var listener = factory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsGlobalKeyboardListener>(listener);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxGlobalKeyboardListener>(listener);
            return;
        }

        Assert.IsType<NullGlobalKeyboardListener>(listener);
    }
}
