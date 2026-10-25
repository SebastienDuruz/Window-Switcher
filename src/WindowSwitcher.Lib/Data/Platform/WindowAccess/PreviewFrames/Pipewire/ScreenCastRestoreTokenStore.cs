namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Pipewire;

/// <summary>
/// Persists ScreenCast portal restore tokens so a selection survives application restarts.
/// </summary>
internal interface IScreenCastRestoreTokenStore
{
    /// <summary>Reads the persisted tokens, keyed by restore key.</summary>
    IReadOnlyDictionary<string, string> Load();

    /// <summary>Replaces the persisted tokens.</summary>
    Task SaveAsync(IReadOnlyDictionary<string, string> tokens, CancellationToken cancellationToken);
}

internal sealed class ConfigScreenCastRestoreTokenStore(ConfigFileAccessor configFileAccessor)
    : IScreenCastRestoreTokenStore
{
    public IReadOnlyDictionary<string, string> Load()
    {
        return configFileAccessor.ReadConfig(config => config.ScreenCastRestoreTokens);
    }

    public Task SaveAsync(
        IReadOnlyDictionary<string, string> tokens,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return configFileAccessor.UpdateConfigAsync(
            config =>
                config.ScreenCastRestoreTokens = new Dictionary<string, string>(
                    tokens,
                    StringComparer.Ordinal
                ),
            cancellationToken
        );
    }
}
