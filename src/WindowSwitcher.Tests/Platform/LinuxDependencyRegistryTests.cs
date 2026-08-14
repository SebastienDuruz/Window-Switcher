using WindowSwitcher.Lib.Data.Platform.Commands.Dependencies;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class LinuxDependencyRegistryTests
{
    [Fact]
    public void ReportMissingOnce_RaisesEventOnlyOnce_CaseInsensitive()
    {
        var sut = new LinuxDependencyRegistry();
        var raised = new List<string>();
        sut.DependencyMissing += dependency => raised.Add(dependency);

        sut.ReportMissingOnce("libpipewire-0.3.so.0");
        sut.ReportMissingOnce("LIBPIPEWIRE-0.3.SO.0");
        sut.ReportMissingOnce(" ");

        Assert.Single(raised);
        Assert.Equal("libpipewire-0.3.so.0", raised[0]);
        Assert.Single(sut.GetReportedMissing());
    }
}
