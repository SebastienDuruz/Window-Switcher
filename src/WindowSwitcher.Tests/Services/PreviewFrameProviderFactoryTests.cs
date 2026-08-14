using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using WindowSwitcher.Lib.Data.Platform.Diagnostics;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class PreviewFrameProviderFactoryTests
{
    [Fact]
    public void Create_SelectsX11ProviderWithoutPipeWireFallback()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.X11,
            supportsX11: true,
            supportsPipeWire: false,
            capabilities: capabilities
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<X11PreviewFrameProvider>(provider);
        Assert.Equal("XComposite", capabilities.Current.PreviewBackend);
        Assert.True(capabilities.Current.PreviewAvailable);
    }

    [Fact]
    public void Create_DoesNotUsePipeWireWhenX11CaptureIsUnavailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.X11,
            supportsX11: false,
            supportsPipeWire: true,
            capabilities: capabilities
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.False(capabilities.Current.PreviewAvailable);
        Assert.Contains("XComposite", capabilities.Current.LastFailure);
    }

    [Fact]
    public void Create_DoesNotUseX11WhenPipeWireIsUnavailableOnWayland()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var dependencies = new FakeLinuxDependencyRegistry();
        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.Wayland,
            supportsX11: true,
            supportsPipeWire: false,
            supportsNativeAdapter: true,
            capabilities: capabilities,
            dependencies: dependencies
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Contains("libpipewire-0.3.so.0", dependencies.ReportedMissing);
        Assert.DoesNotContain("libwindowswitcher-pipewire.so", dependencies.ReportedMissing);
        Assert.Equal("EWMH (XWayland)", capabilities.Current.WindowBackend);
        Assert.False(capabilities.Current.PreviewAvailable);
    }

    [Fact]
    public void Create_ReturnsNoOpForUnsupportedSession()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.Unsupported,
            supportsX11: true,
            supportsPipeWire: true,
            capabilities: capabilities
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Equal("Unavailable", capabilities.Current.WindowBackend);
        Assert.False(capabilities.Current.PreviewAvailable);
    }

    [Fact]
    public void Create_SelectsPipeWireOnlyWhenBothNativeLibrariesAreAvailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.Wayland,
            supportsX11: false,
            supportsPipeWire: true,
            supportsNativeAdapter: true,
            capabilities: capabilities
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<PipeWireFrameProvider>(provider);
        Assert.Equal("PipeWire", capabilities.Current.PreviewBackend);
        Assert.True(capabilities.Current.PreviewAvailable);
    }

    [Fact]
    public void Create_ReportsMissingProjectNativeAdapterSeparately()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var dependencies = new FakeLinuxDependencyRegistry();
        var capabilities = new PlatformCapabilityStatus();
        var factory = CreateFactory(
            LinuxSessionKind.Wayland,
            supportsX11: true,
            supportsPipeWire: true,
            supportsNativeAdapter: false,
            capabilities: capabilities,
            dependencies: dependencies
        );

        using var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.DoesNotContain("libpipewire-0.3.so.0", dependencies.ReportedMissing);
        Assert.Contains("libwindowswitcher-pipewire.so", dependencies.ReportedMissing);
        Assert.Contains("libwindowswitcher-pipewire.so", capabilities.Current.LastFailure);
    }

    private static RuntimePreviewFrameProviderFactory CreateFactory(
        LinuxSessionKind session,
        bool supportsX11,
        bool supportsPipeWire,
        PlatformCapabilityStatus capabilities,
        bool? supportsNativeAdapter = null,
        ILinuxDependencyRegistry? dependencies = null
    )
    {
        return new RuntimePreviewFrameProviderFactory(
            dependencies,
            () => supportsX11,
            () => supportsPipeWire,
            () => supportsNativeAdapter ?? supportsPipeWire,
            () => session,
            variable => variable == "DISPLAY" ? ":1" : null,
            capabilities
        );
    }

    private sealed class FakeLinuxDependencyRegistry : ILinuxDependencyRegistry
    {
        public HashSet<string> ReportedMissing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void ReportMissingOnce(string dependency)
        {
            _ = ReportedMissing.Add(dependency);
        }
    }

    private sealed class FakeWinAccessor : WinAccessorBase
    {
        public override Task<IReadOnlyCollection<WindowConfig>> GetWindowsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyCollection<WindowConfig>>([]);

        public override Task<bool> TryActivateWindowAsync(
            string windowId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);

        public override Task<bool> TryRenameWindowAsync(
            string windowId,
            string windowTitle,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }
}
