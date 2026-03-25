using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class WaylandScreenCastMemoryCacheTests
{
    [Fact]
    public void SetRestoreToken_MapsAllRestoreKeys()
    {
        using var sut = new WaylandScreenCastMemoryCache();
        string[] restoreKeys = ["process_title:proc|title", "window-id"];

        bool changed = sut.SetRestoreToken(restoreKeys, " token ");

        Assert.True(changed);
        Assert.Equal("token", sut.GetRestoreToken(["window-id"]));
        Assert.Equal("token", sut.GetRestoreToken(["process_title:proc|title"]));
    }

    [Fact]
    public void GetRestoreDataCandidates_PreservesRestoreKeyOrder()
    {
        using var sut = new WaylandScreenCastMemoryCache();
        string[] restoreKeys = ["first", "second"];

        sut.SetRestoreData(["second"], "value-2");
        sut.SetRestoreData(["first"], "value-1");

        IReadOnlyList<string> values = sut.GetRestoreDataCandidates(restoreKeys);

        Assert.Equal(["value-1", "value-2"], values);
    }

    [Fact]
    public void ClearArtifacts_RemovesStoredTokenDataAndStreamId()
    {
        using var sut = new WaylandScreenCastMemoryCache();
        string[] restoreKeys = ["window-id"];

        sut.SetRestoreToken(restoreKeys, "token");
        sut.SetRestoreData(restoreKeys, "restore-data");
        sut.SetStreamId(restoreKeys, "stream-id");

        bool changed = sut.ClearArtifacts(restoreKeys);

        Assert.True(changed);
        Assert.Null(sut.GetRestoreToken(restoreKeys));
        Assert.Empty(sut.GetRestoreDataCandidates(restoreKeys));
        Assert.Null(sut.GetStreamId(restoreKeys));
    }
}
