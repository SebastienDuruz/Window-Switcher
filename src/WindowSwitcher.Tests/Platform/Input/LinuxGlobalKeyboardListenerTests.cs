using System.Runtime.Versioning;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform.Input;

[SupportedOSPlatform("linux")]
public sealed class LinuxGlobalKeyboardListenerTests
{
    [Fact]
    public void EnsureKeyboardAccess_DoesNotThrow_WhenAccessibleKeyboardExists()
    {
        var devices = new[]
        {
            new InputDeviceInfo
            {
                Path = "/dev/input/event4",
                Kind = DeviceKind.Keyboard,
                IsAccessible = true,
            },
        };

        LinuxGlobalKeyboardListener.EnsureKeyboardAccess(devices);
    }

    [Fact]
    public void EnsureKeyboardAccess_ThrowsDiagnostic_WhenDevicesAreInaccessible()
    {
        var devices = new[]
        {
            new InputDeviceInfo
            {
                Path = "/dev/input/event0",
                IsAccessible = false,
                AccessError = "Permission denied",
            },
            new InputDeviceInfo
            {
                Path = "/dev/input/event1",
                IsAccessible = false,
                AccessError = "Permission denied",
            },
        };

        LinuxInputAccessException exception = Assert.Throws<LinuxInputAccessException>(
            () => LinuxGlobalKeyboardListener.EnsureKeyboardAccess(devices)
        );

        Assert.Contains("/dev/input/event*", exception.Message, StringComparison.Ordinal);
        Assert.Contains("input group", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["/dev/input/event0", "/dev/input/event1"], exception.DevicePaths);
    }
}
