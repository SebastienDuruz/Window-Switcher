using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux.InputEventsCore.Linux;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Linux;

internal interface ILinuxInputDeviceDiscovery
{
    Task<IReadOnlyList<InputDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);
}

internal interface ILinuxKeyboardForwarder : IDisposable
{
    void Forward(NativeInputEvent nativeEvent);
    void Forward(IEnumerable<NativeInputEvent> nativeEvents);
}

internal interface ILinuxKeyboardForwarderFactory
{
    Task<ILinuxKeyboardForwarder> CreateAsync(
        IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
        CancellationToken cancellationToken
    );
}

internal sealed class LinuxKeyboardForwarderFactory : ILinuxKeyboardForwarderFactory
{
    public async Task<ILinuxKeyboardForwarder> CreateAsync(
        IReadOnlyCollection<InputDeviceInfo> keyboardDevices,
        CancellationToken cancellationToken
    )
    {
        LinuxUinputKeyboardForwarder forwarder = await Task.Run(
                () => new LinuxUinputKeyboardForwarder(keyboardDevices),
                cancellationToken
            )
            .ConfigureAwait(false);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
            return forwarder;
        }
        catch
        {
            forwarder.Dispose();
            throw;
        }
    }
}
