using WindowSwitcher.Lib.Data.Platform.Commands.Abstractions;
using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxDependencyRegistryTests
{
    [Fact]
    public void IsWmctrlAvailable_UsesCachedResult()
    {
        var which = new TrackingCommandWrapper(arg => arg == "wmctrl" ? "/usr/bin/wmctrl" : "");
        var gstInspect = new TrackingCommandWrapper(_ => string.Empty);
        var sut = new LinuxDependencyRegistry(which, gstInspect);

        bool first = sut.IsWmctrlAvailable;
        bool second = sut.IsWmctrlAvailable;

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, which.CountCallsFor("wmctrl"));
    }

    [Fact]
    public void IsGstPipeWireSrcAvailable_ReturnsFalse_WhenGstLaunchIsMissing()
    {
        var which = new TrackingCommandWrapper(_ => string.Empty);
        var gstInspect = new TrackingCommandWrapper(_ => "should not be called");
        var sut = new LinuxDependencyRegistry(which, gstInspect);

        bool available = sut.IsGstPipeWireSrcAvailable;

        Assert.False(available);
        Assert.Equal(1, which.CountCallsFor("gst-launch-1.0"));
        Assert.Empty(gstInspect.Calls);
    }

    [Fact]
    public void IsGstPipeWireSrcAvailable_UsesCachedResult()
    {
        var which = new TrackingCommandWrapper(arg =>
            arg == "gst-launch-1.0" ? "/usr/bin/gst-launch-1.0" : string.Empty
        );
        var gstInspect = new TrackingCommandWrapper(_ => "Factory Details: pipewiresrc");
        var sut = new LinuxDependencyRegistry(which, gstInspect);

        bool first = sut.IsGstPipeWireSrcAvailable;
        bool second = sut.IsGstPipeWireSrcAvailable;

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(1, which.CountCallsFor("gst-launch-1.0"));
        Assert.Single(gstInspect.Calls);
    }

    [Fact]
    public void ReportMissingOnce_RaisesEventOnlyOnce_CaseInsensitive()
    {
        var sut = new LinuxDependencyRegistry(new TrackingCommandWrapper(_ => string.Empty));
        var raised = new List<string>();
        sut.DependencyMissing += dependency => raised.Add(dependency);

        sut.ReportMissingOnce("pw-dump");
        sut.ReportMissingOnce("PW-DUMP");
        sut.ReportMissingOnce(" ");

        Assert.Single(raised);
        Assert.Equal("pw-dump", raised[0]);
        Assert.Single(sut.GetReportedMissing());
    }

    private sealed class TrackingCommandWrapper(Func<string, string> execute) : ICommandWrapper
    {
        public List<string> Calls { get; } = [];

        public string Execute(string args)
        {
            Calls.Add(args);
            return execute(args);
        }

        public int CountCallsFor(string argument)
        {
            return Calls.Count(call => string.Equals(call, argument, StringComparison.Ordinal));
        }
    }
}
