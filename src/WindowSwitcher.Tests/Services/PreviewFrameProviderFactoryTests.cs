using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
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
    public void WindowsFactory_ReturnsNoOpProvider()
    {
        var factory = new WindowsPreviewFrameProviderFactory();

        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
    }

    [Fact]
    public void LinuxFactory_ReturnsNoOpProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstPipeWireSrcAvailable = false,
            IsPwDumpAvailable = true,
        };

        var factory = new LinuxPreviewFrameProviderFactory(dependencies);
        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Contains("gstreamer-pipewire", dependencies.ReportedMissing);
    }

    [Fact]
    public void RuntimeFactory_ReturnsNoOpProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstLaunchAvailable = false,
            IsGstPipeWireSrcAvailable = false,
            IsPwDumpAvailable = false,
        };

        var factory = new RuntimePreviewFrameProviderFactory(dependencies);
        var provider = factory.Create(new FakeWinAccessor());

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<NoOpPreviewFrameProvider>(provider);
            Assert.Contains("gstreamer-pipewire", dependencies.ReportedMissing);
        }
        else
        {
            Assert.IsType<NoOpPreviewFrameProvider>(provider);
        }
    }

    [Fact]
    public void LinuxFactory_ReturnsX11Provider_WhenAccessorIsX11AndNativeCaptureIsSupported()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstLaunchAvailable = false,
            IsGstPipeWireSrcAvailable = false,
            IsPwDumpAvailable = false,
        };

        var factory = new LinuxPreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => true
        );
        var provider = factory.Create(new X11WinAccessor());

        Assert.IsType<X11PreviewFrameProvider>(provider);
        Assert.Empty(dependencies.ReportedMissing);
    }

    [Fact]
    public void LinuxFactory_ReturnsNoOpProvider_WhenX11CaptureIsUnavailable()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstLaunchAvailable = true,
            IsGstPipeWireSrcAvailable = true,
            IsPwDumpAvailable = true,
        };

        var factory = new LinuxPreviewFrameProviderFactory(
            dependencies,
            supportsX11PreviewCapture: () => false
        );
        var provider = factory.Create(new X11WinAccessor());

        Assert.IsType<NoOpPreviewFrameProvider>(provider);
        Assert.Empty(dependencies.ReportedMissing);
    }

    private sealed class FakeLinuxDependencyRegistry : ILinuxDependencyRegistry
    {
        public bool IsWmctrlAvailable { get; init; }
        public bool IsGstLaunchAvailable { get; init; }
        public bool IsPwDumpAvailable { get; init; }
        public bool IsGdbusAvailable { get; init; }
        public bool IsGstPipeWireSrcAvailable { get; init; }
        public HashSet<string> ReportedMissing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void ReportMissingOnce(string dependency)
        {
            _ = ReportedMissing.Add(dependency);
        }
    }

    private sealed class FakeWinAccessor : WinAccessorBase
    {
        public override ObservableCollection<WindowConfig> GetWindows()
        {
            return [];
        }

        public override void RaiseWindow(string windowId) { }

        public override Bitmap? TakeScreenshot(string windowId)
        {
            return null;
        }

        public override void RenameWindowTitle(string windowId, string windowTitle) { }
    }
}
