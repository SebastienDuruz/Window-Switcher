using WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;
using Xunit;

namespace WindowSwitcher.Tests.Platform;

public sealed class WaylandScreenCastRestoreTokenPersistenceTests
{
    [Fact]
    public void Constructor_RestoresPersistedTokens()
    {
        var store = new FakeStore
        {
            Persisted = new Dictionary<string, string>
            {
                ["title:calculator"] = "token-a",
                ["process_title:kcalc|calculator"] = "token-a",
            },
        };
        using var cache = new WaylandScreenCastMemoryCache(store);

        string? token = cache.TakeRestoreToken(["title:calculator", "window:kwin:1"]);

        Assert.Equal("token-a", token);
    }

    [Fact]
    public async Task PersistAsync_SavesTitleKeysButNotVolatileWindowKeys()
    {
        var store = new FakeStore();
        using var cache = new WaylandScreenCastMemoryCache(store);
        cache.SetRestoreToken(["title:calculator", "window:0x01e00007"], "token-b");

        await cache.PersistAsync(CancellationToken.None);

        KeyValuePair<string, string> saved = Assert.Single(store.Persisted);
        Assert.Equal("title:calculator", saved.Key);
        Assert.Equal("token-b", saved.Value);
    }

    [Fact]
    public async Task PersistAsync_SavesAnEmptySetAfterClear()
    {
        var store = new FakeStore
        {
            Persisted = new Dictionary<string, string> { ["title:calculator"] = "token-c" },
        };
        using var cache = new WaylandScreenCastMemoryCache(store);

        cache.Clear();
        await cache.PersistAsync(CancellationToken.None);

        Assert.Empty(store.Persisted);
        Assert.Equal(1, store.SaveCalls);
    }

    [Fact]
    public async Task PersistAsync_DoesNothingWithoutStore()
    {
        using var cache = new WaylandScreenCastMemoryCache();
        cache.SetRestoreToken(["title:calculator"], "token-d");

        await cache.PersistAsync(CancellationToken.None);
    }

    private sealed class FakeStore : IScreenCastRestoreTokenStore
    {
        public IReadOnlyDictionary<string, string> Persisted { get; set; } =
            new Dictionary<string, string>();
        public int SaveCalls { get; private set; }

        public IReadOnlyDictionary<string, string> Load() => Persisted;

        public Task SaveAsync(
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken
        )
        {
            SaveCalls++;
            Persisted = new Dictionary<string, string>(tokens);
            return Task.CompletedTask;
        }
    }
}
