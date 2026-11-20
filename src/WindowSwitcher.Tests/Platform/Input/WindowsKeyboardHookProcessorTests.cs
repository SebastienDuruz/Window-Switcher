using Serilog.Events;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Tests.TestLogging;
using Xunit;
using static WindowSwitcher.Lib.Data.Platform.Keybinds.Listeners.Windows.WindowsKeyboardHookProcessor;

namespace WindowSwitcher.Tests.Platform.Input;

/// <summary>
/// Covers the low-level keyboard hook decisions without installing a hook, so they also run on
/// Linux. Hook input mirrors <c>KBDLLHOOKSTRUCT</c>: virtual key, scan code and flags.
/// </summary>
public sealed class WindowsKeyboardHookProcessorTests
{
    private const uint VkA = 0x41;
    private const uint VkReturn = 0x0D;
    private const uint ScanCodeReturn = 0x1C;
    private const uint WmChar = 0x0102;

    [Theory]
    [InlineData(WmKeyDown, GlobalKeyState.Down)]
    [InlineData(WmSysKeyDown, GlobalKeyState.Down)]
    [InlineData(WmKeyUp, GlobalKeyState.Up)]
    [InlineData(WmSysKeyUp, GlobalKeyState.Up)]
    public void KeyMessages_CreateEventsWithTheMatchingState(uint message, GlobalKeyState expected)
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);

        GlobalKeyEventArgs? keyEvent = sut.TryCreateEvent(message, VkA, 0x1E, flags: 0);

        Assert.NotNull(keyEvent);
        Assert.Equal("Windows", keyEvent.Platform);
        Assert.Equal("VK_41", keyEvent.KeyCode);
        Assert.Equal(expected, keyEvent.State);
    }

    [Fact]
    public void OtherMessages_ArePassedThrough()
    {
        Assert.Null(CreateSut(out _).TryCreateEvent(WmChar, VkA, 0x1E, flags: 0));
    }

    [Fact]
    public void InjectedKeys_ArePassedThrough_SoForwardedKeysDoNotLoopBack()
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);

        Assert.Null(sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, LlkhfInjected));
        // An injected press must not count as held either.
        Assert.False(sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, flags: 0)!.IsRepeat);
    }

    [Fact]
    public void HeldKey_IsReportedAsRepeatUntilReleased()
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);

        bool first = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!.IsRepeat;
        bool second = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!.IsRepeat;
        bool release = sut.TryCreateEvent(WmKeyUp, VkA, 0x1E, 0)!.IsRepeat;
        bool afterRelease = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!.IsRepeat;

        Assert.Equal([false, true, false, false], [first, second, release, afterRelease]);
    }

    [Fact]
    public void Reset_ForgetsHeldKeys()
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);
        _ = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0);

        sut.Reset();

        Assert.False(sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!.IsRepeat);
    }

    [Theory]
    [InlineData(0u, false, 0x001C0000)]
    [InlineData(LlkhfExtended, true, 0x011C0000)]
    public void ExtendedFlag_IsForwardedToTheEventAndTheKeyNameLookup(
        uint flags,
        bool expectedExtended,
        int expectedKeyNameParameter
    )
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out List<int> keyNameRequests);

        GlobalKeyEventArgs keyEvent = sut.TryCreateEvent(
            WmKeyDown,
            VkReturn,
            ScanCodeReturn,
            flags
        )!;

        Assert.Equal(expectedExtended, keyEvent.IsExtendedKey);
        Assert.Equal([expectedKeyNameParameter], keyNameRequests);
        Assert.Equal("Key 0x" + expectedKeyNameParameter.ToString("X8"), keyEvent.KeyName);
    }

    [Fact]
    public void ShouldConsume_ForwardsWithoutFilter()
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);
        GlobalKeyEventArgs keyEvent = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!;

        Assert.False(sut.ShouldConsume(keyEvent, filter: null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldConsume_FollowsTheFilterDecision(bool consume)
    {
        WindowsKeyboardHookProcessor sut = CreateSut(out _);
        GlobalKeyEventArgs keyEvent = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!;
        var filter = new FixedFilter(
            consume ? KeyboardFilterDecision.Consume() : KeyboardFilterDecision.Forward()
        );

        Assert.Equal(consume, sut.ShouldConsume(keyEvent, filter));
    }

    [Fact]
    public void ShouldConsume_ForwardsAndLogs_WhenTheFilterFails()
    {
        var sut = new WindowsKeyboardHookProcessor(
            _ => null,
            TestLogger.Create(out CollectingSink logs)
        );
        GlobalKeyEventArgs keyEvent = sut.TryCreateEvent(WmKeyDown, VkA, 0x1E, 0)!;

        bool consumed = sut.ShouldConsume(keyEvent, new ThrowingFilter());

        Assert.False(consumed);
        Assert.True(logs.Contains(LogEventLevel.Error, "Windows keyboard filter failed"));
    }

    private static WindowsKeyboardHookProcessor CreateSut(out List<int> keyNameRequests)
    {
        List<int> requests = [];
        keyNameRequests = requests;
        return new WindowsKeyboardHookProcessor(
            parameter =>
            {
                requests.Add(parameter);
                return "Key 0x" + parameter.ToString("X8");
            },
            Serilog.Core.Logger.None
        );
    }

    private sealed class FixedFilter(KeyboardFilterDecision decision) : IKeyboardInputFilter
    {
        public KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent) => decision;

        public void Reset() { }
    }

    private sealed class ThrowingFilter : IKeyboardInputFilter
    {
        public KeyboardFilterDecision ProcessEvent(GlobalKeyEventArgs keyEvent) =>
            throw new InvalidOperationException("filter bug");

        public void Reset() { }
    }
}
