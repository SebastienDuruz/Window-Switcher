using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using WindowSwitcherLib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories;
using WindowSwitcherLib.Data.Platform.WindowAccess.Factories.Abstractions;
using WindowSwitcherLib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;
using WindowSwitcherLib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class StaticFactoriesTests
{
    [Fact]
    public void AccessorFactory_Current_ThrowsOnNullAssignment()
    {
        Assert.Throws<ArgumentNullException>(() => AccessorFactory.Current = null!);
    }

    [Fact]
    public void AccessorFactory_GetAccessor_DelegatesToCurrentFactory()
    {
        IWinAccessorFactory previous = AccessorFactory.Current;
        var expectedAccessor = new FakeWinAccessor();
        var fakeFactory = new FakeWinAccessorFactory(expectedAccessor);

        try
        {
            AccessorFactory.Current = fakeFactory;

            WinAccessorBase accessor = AccessorFactory.GetAccessor();

            Assert.Same(expectedAccessor, accessor);
            Assert.Equal(1, fakeFactory.CreateCalls);
        }
        finally
        {
            AccessorFactory.Current = previous;
        }
    }

    [Fact]
    public void PreviewFactory_Current_ThrowsOnNullAssignment()
    {
        Assert.Throws<ArgumentNullException>(() => PreviewFactory.Current = null!);
    }

    [Fact]
    public void PreviewFactory_Create_DelegatesToCurrentFactory()
    {
        IPreviewFrameProviderFactory previous = PreviewFactory.Current;
        var expectedProvider = new FakePreviewProvider();
        var fakeFactory = new FakePreviewFrameProviderFactory(expectedProvider);
        var accessor = new FakeWinAccessor();

        try
        {
            PreviewFactory.Current = fakeFactory;

            IPreviewFrameProvider provider = PreviewFactory.Create(accessor);

            Assert.Same(expectedProvider, provider);
            Assert.Equal(1, fakeFactory.CreateCalls);
            Assert.Same(accessor, fakeFactory.LastAccessor);
        }
        finally
        {
            PreviewFactory.Current = previous;
        }
    }

    private sealed class FakeWinAccessorFactory(WinAccessorBase accessor) : IWinAccessorFactory
    {
        public int CreateCalls { get; private set; }

        public WinAccessorBase Create()
        {
            CreateCalls++;
            return accessor;
        }
    }

    private sealed class FakePreviewFrameProviderFactory(IPreviewFrameProvider provider)
        : IPreviewFrameProviderFactory
    {
        public int CreateCalls { get; private set; }
        public WinAccessorBase? LastAccessor { get; private set; }

        public IPreviewFrameProvider Create(WinAccessorBase accessorBase)
        {
            CreateCalls++;
            LastAccessor = accessorBase;
            return provider;
        }
    }

    private sealed class FakePreviewProvider : IPreviewFrameProvider
    {
        public Task<Bitmap?> RequestAsync(
            string windowId,
            ScreenshotRequest request,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult<Bitmap?>(null);
        }

        public void ForgetWindow(string windowId) { }

        public void Dispose() { }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
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
