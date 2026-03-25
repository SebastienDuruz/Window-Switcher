using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Media.Imaging;
using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors.Abstractions;
using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using WindowSwitcher.Lib.Models;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class PipeWireFrameProviderPortalSelectionTests
{
    public PipeWireFrameProviderPortalSelectionTests()
    {
        WaylandScreenCastMemoryCache.Shared.Clear();
    }

    [Fact]
    public void SelectPortalStreamNodeId_AcceptsExactWindowTitleMatch()
    {
        using var sut = CreateSut(
            new WindowConfig
            {
                WindowId = "0x100",
                ProcessName = "app",
                WindowTitle = "Alpha Window",
            },
            new WindowConfig
            {
                WindowId = "0x200",
                ProcessName = "app",
                WindowTitle = "Beta Window",
            }
        );

        string? nodeId = InvokeSelectPortalStreamNodeId(
            sut,
            "0x100",
            BuildPortalResults(
                (
                    55u,
                    new Dictionary<string, object>
                    {
                        ["title"] = "Alpha Window",
                        ["mapping_id"] = "stream-alpha",
                    }
                )
            ),
            out string? stableId,
            out bool shouldPersistStableId
        );

        Assert.Equal("55", nodeId);
        Assert.Equal("stream-alpha", stableId);
        Assert.True(shouldPersistStableId);
    }

    [Fact]
    public void SelectPortalStreamNodeId_RejectsIdOnlyMatchWhenTitleBelongsToAnotherWindow()
    {
        using var sut = CreateSut(
            new WindowConfig
            {
                WindowId = "0x100",
                ProcessName = "app",
                WindowTitle = "Alpha Window",
            },
            new WindowConfig
            {
                WindowId = "0x200",
                ProcessName = "app",
                WindowTitle = "Beta Window",
            }
        );

        string? nodeId = InvokeSelectPortalStreamNodeId(
            sut,
            "0x100",
            BuildPortalResults(
                (
                    55u,
                    new Dictionary<string, object>
                    {
                        ["title"] = "Beta Window",
                        ["source_window"] = "0x100",
                        ["mapping_id"] = "stream-beta",
                    }
                )
            ),
            out _,
            out bool shouldPersistStableId
        );

        Assert.Null(nodeId);
        Assert.False(shouldPersistStableId);
    }

    [Fact]
    public void SelectPortalStreamNodeId_RejectsSharedTitleTokenWhenExactTitleBelongsToAnotherWindow()
    {
        using var sut = CreateSut(
            new WindowConfig
            {
                WindowId = "0x300",
                ProcessName = "app",
                WindowTitle = "Alpha Gamma",
            },
            new WindowConfig
            {
                WindowId = "0x400",
                ProcessName = "app",
                WindowTitle = "Alpha Delta",
            }
        );

        string? nodeId = InvokeSelectPortalStreamNodeId(
            sut,
            "0x300",
            BuildPortalResults(
                (
                    56u,
                    new Dictionary<string, object>
                    {
                        ["title"] = "Alpha Delta",
                        ["mapping_id"] = "stream-delta",
                    }
                )
            ),
            out _,
            out bool shouldPersistStableId
        );

        Assert.Null(nodeId);
        Assert.False(shouldPersistStableId);
    }

    private static PipeWireFrameProvider CreateSut(params WindowConfig[] windows)
    {
        return new PipeWireFrameProvider(
            new FakeWinAccessor(windows),
            new FakePwDumpWrapper("[]"),
            new FakeGdbusWrapper(),
            new FakeGstLaunchWrapper()
        );
    }

    private static Dictionary<string, object> BuildPortalResults(
        params (uint NodeId, IDictionary<string, object> Properties)[] streams
    )
    {
        var boxedStreams = new object[streams.Length];
        for (int index = 0; index < streams.Length; index++)
        {
            boxedStreams[index] = (streams[index].NodeId, streams[index].Properties);
        }

        return new Dictionary<string, object> { ["streams"] = boxedStreams };
    }

    private static string? InvokeSelectPortalStreamNodeId(
        PipeWireFrameProvider provider,
        string windowId,
        IDictionary<string, object> results,
        out string? selectedStreamStableId,
        out bool shouldPersistSelectedStreamStableId
    )
    {
        MethodInfo method =
            typeof(PipeWireFrameProvider).GetMethod(
                "SelectPortalStreamNodeId",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("SelectPortalStreamNodeId was not found.");

        object?[] arguments = [windowId, results, null, false];
        string? nodeId = method.Invoke(provider, arguments) as string;
        selectedStreamStableId = arguments[2] as string;
        shouldPersistSelectedStreamStableId =
            arguments[3] is bool typedValue && typedValue;
        return nodeId;
    }

    private sealed class FakeWinAccessor(params WindowConfig[] windows) : WinAccessorBase
    {
        private readonly WindowConfig[] _windows = windows.Select(window => window.Clone()).ToArray();

        public override ObservableCollection<WindowConfig> GetWindows()
        {
            return new ObservableCollection<WindowConfig>(
                _windows.Select(window => window.Clone()).ToList()
            );
        }

        public override void RaiseWindow(string windowId) { }

        public override Bitmap? TakeScreenshot(string windowId)
        {
            return null;
        }

        public override void RenameWindowTitle(string windowId, string windowTitle) { }
    }

    private sealed class FakePwDumpWrapper(string output) : IPwDumpWrapper
    {
        public string Execute(string args)
        {
            return output;
        }

        public string Execute(int timeoutMs)
        {
            return output;
        }
    }

    private sealed class FakeGdbusWrapper : IGdbusWrapper
    {
        public string Execute(string args)
        {
            return string.Empty;
        }

        public string Execute(IReadOnlyList<string> args, int timeoutMs)
        {
            return string.Empty;
        }
    }

    private sealed class FakeGstLaunchWrapper : IGstLaunchWrapper
    {
        public string Execute(string args)
        {
            return string.Empty;
        }

        public Process? StartPipeWireRawBgraStream(
            string nodeId,
            int widthPx,
            int heightPx,
            int? pipeWireRemoteFd = null
        )
        {
            return null;
        }
    }
}
