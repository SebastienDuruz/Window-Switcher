using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Factories;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.NoOp;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.X11;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class PreviewFrameProviderFactoryTests
{
    [Fact]
    public void WindowsFactory_ReturnsNoOpProvider()
    {
        var factory = new WindowsPreviewFrameProviderFactory();

        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
    }

    [Fact]
    public void LinuxFactory_ReturnsNoOpProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry();

        var factory = new LinuxPreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => false,
            supportsPipeWire: () => false,
            sessionTypeResolver: () => "wayland"
        );
        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Contains("libpipewire-0.3.so.0", dependencies.ReportedMissing);
    }

    [Fact]
    public void RuntimeFactory_ReturnsNoOpProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry();

        var factory = new RuntimePreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => false,
            supportsPipeWire: () => false,
            sessionTypeResolver: () => "wayland"
        );
        var provider = factory.Create(new FakeWinAccessor());

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<NoOpPreviewFrameProvider>(provider);
            Assert.Contains("libpipewire-0.3.so.0", dependencies.ReportedMissing);
        }
        else
        {
            Assert.IsType<NoOpPreviewFrameProvider>(provider);
        }
    }

    [Fact]
    public void LinuxFactory_ReturnsX11Provider_WhenAccessorIsX11AndNativeCaptureIsSupported()
    {
        var dependencies = new FakeLinuxDependencyRegistry();

        var factory = new LinuxPreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => true,
            sessionTypeResolver: () => "x11"
        );
        using var accessor = new X11EwmhWindowAccessor(new FakeX11EwmhClient());
        var provider = factory.Create(accessor);

        Assert.IsType<X11PreviewFrameProvider>(provider);
        Assert.Empty(dependencies.ReportedMissing);
    }

    [Fact]
    public void LinuxFactory_ReturnsNoOpProvider_WhenX11CaptureIsUnavailable()
    {
        var dependencies = new FakeLinuxDependencyRegistry();

        var factory = new LinuxPreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => false,
            sessionTypeResolver: () => "x11"
        );
        using var accessor = new X11EwmhWindowAccessor(new FakeX11EwmhClient());
        var provider = factory.Create(accessor);

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Empty(dependencies.ReportedMissing);
    }

    private sealed class FakeLinuxDependencyRegistry : ILinuxDependencyRegistry
    {
        public HashSet<string> ReportedMissing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void ReportMissingOnce(string dependency)
        {
            _ = ReportedMissing.Add(dependency);
        }
    }

    private sealed class FakeX11EwmhClient : IX11EwmhClient
    {
        public IReadOnlyList<X11EwmhWindow> GetWindows() => [];

        public bool TryActivateWindow(uint windowId) => true;

        public bool TryRenameWindow(uint windowId, string title) => true;

        public void Dispose() { }
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
