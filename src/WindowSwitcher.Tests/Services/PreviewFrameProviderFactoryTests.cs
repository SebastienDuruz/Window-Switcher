using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Data.Platform.Commands.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Screenshots;
using WindowSwitcherLib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Services;

public sealed class PreviewFrameProviderFactoryTests
{
    [Fact]
    public void LinuxFactory_ReturnsScreenshotProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstLaunchAvailable = false,
            IsGstPipeWireSrcAvailable = true,
            IsPwDumpAvailable = true,
        };

        var factory = new LinuxPreviewFrameProviderFactory(dependencies);
        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<ScreenshotPreviewFrameProvider>(provider);
        Assert.Contains("gst-launch-1.0", dependencies.ReportedMissing);
    }

    [Fact]
    public void RuntimeFactory_ReturnsScreenshotProvider_WhenPipeWireDependenciesAreMissing()
    {
        var dependencies = new FakeLinuxDependencyRegistry
        {
            IsGstLaunchAvailable = false,
            IsGstPipeWireSrcAvailable = false,
            IsPwDumpAvailable = false,
        };

        var factory = new RuntimePreviewFrameProviderFactory(dependencies);
        var provider = factory.Create(new FakeWinAccessor());

        Assert.IsType<ScreenshotPreviewFrameProvider>(provider);

        if (OperatingSystem.IsLinux())
            Assert.Contains("gst-launch-1.0", dependencies.ReportedMissing);
    }

    private sealed class FakeLinuxDependencyRegistry : ILinuxDependencyRegistry
    {
        public event Action<string>? DependencyMissing;

        public bool IsWmctrlAvailable { get; init; }
        public bool IsImportAvailable { get; init; }
        public bool IsGstLaunchAvailable { get; init; }
        public bool IsPwDumpAvailable { get; init; }
        public bool IsGdbusAvailable { get; init; }
        public bool IsGstPipeWireSrcAvailable { get; init; }
        public HashSet<string> ReportedMissing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> GetReportedMissing()
        {
            return ReportedMissing.ToArray();
        }

        public void ReportMissingOnce(string dependency)
        {
            if (!ReportedMissing.Add(dependency))
                return;

            DependencyMissing?.Invoke(dependency);
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
