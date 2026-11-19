using WindowSwitcher.Lib.Data.Platform.WindowAccess.Accessors;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class ProcessNameCacheTests
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(5);

    [Fact]
    public void GetName_ReusesCachedNameWithinTimeToLive()
    {
        var reader = new CountingReader(pid => $"proc{pid}");
        var clock = new ManualTimeProvider();
        var sut = new ProcessNameCache(reader.Read, clock, TimeToLive);

        Assert.Equal("proc42", sut.GetName(42));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal("proc42", sut.GetName(42));

        Assert.Equal(1, reader.ReadCount(42));
    }

    [Fact]
    public void GetName_ReadsAgainAfterExpiration()
    {
        var reader = new CountingReader(pid => $"proc{pid}");
        var clock = new ManualTimeProvider();
        var sut = new ProcessNameCache(reader.Read, clock, TimeToLive);

        _ = sut.GetName(42);
        clock.Advance(TimeToLive);
        _ = sut.GetName(42);

        Assert.Equal(2, reader.ReadCount(42));
    }

    [Fact]
    public void GetName_CachesFailedReads()
    {
        var reader = new CountingReader(_ => string.Empty);
        var sut = new ProcessNameCache(reader.Read, new ManualTimeProvider(), TimeToLive);

        for (int i = 0; i < 10; i++)
            Assert.Equal(string.Empty, sut.GetName(7));

        Assert.Equal(1, reader.ReadCount(7));
    }

    [Fact]
    public void GetName_ClearsCacheAboveMaximumEntries()
    {
        const int maximumEntries = 512;
        var reader = new CountingReader(pid => $"proc{pid}");
        var sut = new ProcessNameCache(
            reader.Read,
            new ManualTimeProvider(),
            TimeToLive,
            maximumEntries
        );

        for (uint pid = 1; pid <= maximumEntries; pid++)
            _ = sut.GetName(pid);
        _ = sut.GetName(1);
        Assert.Equal(1, reader.ReadCount(1));

        _ = sut.GetName(maximumEntries + 1);
        _ = sut.GetName(1);

        Assert.Equal(2, reader.ReadCount(1));
    }

    [Theory]
    [InlineData(@"C:\x\EVE.EXE", "EVE")]
    [InlineData(@"C:\Program Files\App\foo.bar.exe", "foo.bar")]
    [InlineData("noext", "noext")]
    [InlineData(@"C:\tools\script.com", "script.com")]
    [InlineData(@"\\?\C:\x\app.Exe", "app")]
    public void ProcessNameFromImagePath_MatchesProcessNameFormat(string imagePath, string expected)
    {
        Assert.Equal(expected, WindowsProcessNameReader.ProcessNameFromImagePath(imagePath));
    }

    private sealed class CountingReader(Func<uint, string> read)
    {
        private readonly Dictionary<uint, int> _counts = [];

        public string Read(uint processId)
        {
            _counts[processId] = ReadCount(processId) + 1;
            return read(processId);
        }

        public int ReadCount(uint processId) => _counts.GetValueOrDefault(processId);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan delay) => _timestamp += delay.Ticks;
    }
}
