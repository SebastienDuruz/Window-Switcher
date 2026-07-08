using System.Diagnostics;
using Newtonsoft.Json;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Models;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;
using WindowSwitcher.Lib.Models;

namespace WindowSwitcher.Lib.Data;

public class ConfigFileAccessor
{
    private static readonly Lazy<ConfigFileAccessor> Instance = new(() => new ConfigFileAccessor());
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _fileWriteGate = new(1, 1);
    private readonly string _filePath;
    private ConfigFile _config;
    private ConfigLoadFailure? _lastReadFailure;
    private long _configVersion;

    private ConfigFileAccessor()
    {
        StaticData.CheckFolders();
        _filePath = Path.Combine(StaticData.DataFolder, "config.json");
        _config = new ConfigFile();
        ReadUserSettings();
    }

    public static ConfigFileAccessor GetInstance()
    {
        return Instance.Value;
    }

    public string GetFilePath()
    {
        return _filePath;
    }

    /// <summary>
    /// Returns and clears the most recent configuration load failure.
    /// </summary>
    /// <returns>The most recent load failure, or <see langword="null" /> when none was recorded.</returns>
    public ConfigLoadFailure? ConsumeLastReadFailure()
    {
        lock (_syncRoot)
        {
            ConfigLoadFailure? failure = _lastReadFailure;
            _lastReadFailure = null;
            return failure;
        }
    }

    private void ReadUserSettings()
    {
        lock (_syncRoot)
        {
            bool shouldPersistConfig = false;
            if (File.Exists(_filePath))
            {
                try
                {
                    string fileContents = File.ReadAllText(_filePath);
                    _config =
                        JsonConvert.DeserializeObject<ConfigFile>(fileContents) ?? new ConfigFile();
                }
                catch (JsonException ex)
                {
                    RecordReadFailureLocked(ex, "invalid_json");
                    _config = new ConfigFile();
                    WriteUserSettingsLocked();
                }
                catch (IOException ex)
                {
                    RecordReadFailureLocked(ex, "io_error");
                    _config = new ConfigFile();
                    WriteUserSettingsLocked();
                }
                catch (UnauthorizedAccessException ex)
                {
                    RecordReadFailureLocked(ex, "access_denied");
                    _config = new ConfigFile();
                    WriteUserSettingsLocked();
                }
                catch (Exception ex)
                {
                    RecordReadFailureLocked(ex, "unexpected_error");
                    _config = new ConfigFile();
                    WriteUserSettingsLocked();
                }
            }
            else
            {
                _config = new ConfigFile();
                WriteUserSettingsLocked();
            }

            string configJsonBeforeNormalization = SerializeConfig(_config);
            _config = NormalizeConfig(_config);
            if (
                !string.Equals(
                    configJsonBeforeNormalization,
                    SerializeConfig(_config),
                    StringComparison.Ordinal
                )
            )
                shouldPersistConfig = true;

            if (shouldPersistConfig)
                WriteUserSettingsLocked();
        }
    }

    public void WriteUserSettings()
    {
        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        WriteConfigAtomicIfCurrent(snapshot, snapshotVersion);
    }

    /// <summary>
    /// Asynchronously persists the current user settings.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    public async Task WriteUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        await WriteConfigAtomicIfCurrentAsync(snapshot, snapshotVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    public ConfigFile Config
    {
        get
        {
            lock (_syncRoot)
            {
                return CloneConfig(_config);
            }
        }
    }

    public void SaveFloatingWindowSettings(WindowConfig windowConfig)
    {
        string configKey = CreateConfigKey(windowConfig);
        UpdateConfig(config =>
        {
            WindowConfig persistedConfig = windowConfig.Clone();
            persistedConfig.WindowId = string.Empty;
            persistedConfig.ConfigKey = configKey;
            config.FloatingWindowsConfig.RemoveAll(x => x != null && x.ConfigKey == configKey);
            config.FloatingWindowsConfig.Add(persistedConfig);
        });
    }

    public void ResetUserSettings()
    {
        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            _config = NormalizeConfig(new ConfigFile());
            MarkConfigChangedLocked();
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        WriteConfigAtomicIfCurrent(snapshot, snapshotVersion);
    }

    public void ResetFloatingWindowSettings()
    {
        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            _config.FloatingWindowsConfig.Clear();
            MarkConfigChangedLocked();
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        WriteConfigAtomicIfCurrent(snapshot, snapshotVersion);
    }

    public void SavePrefixesList(List<string> prefixes)
    {
        UpdateConfig(config => config.WhitelistPrefixes = prefixes.ToList());
    }

    public void SaveBlacklist(List<string> blacklist)
    {
        UpdateConfig(config => config.BlacklistPrefixes = blacklist.ToList());
    }

    public void SaveWindowKeybindTargets(IReadOnlyCollection<WindowKeybindTargetConfig> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        UpdateConfig(config =>
            config.WindowKeybindTargets = KeybindCatalogBuilder
                .Build(targets)
                .Targets.Select(KeybindCatalogBuilder.CloneTarget)
                .ToList()
        );
    }

    public WindowConfig? GetFloatingWindowConfig(WindowConfig windowConfig)
    {
        ConfigFile? snapshot = null;
        long snapshotVersion = 0;
        WindowConfig? persistedConfig;
        lock (_syncRoot)
        {
            bool shouldPersistConfig = false;
            string configKey = CreateConfigKey(windowConfig);
            WindowConfig? existingConfig = _config.FloatingWindowsConfig.FirstOrDefault(x =>
                x != null && x.ConfigKey == configKey
            );

            if (existingConfig == null)
            {
                string normalizedTitle = NormalizeKeyPart(windowConfig.WindowTitle);
                existingConfig = _config.FloatingWindowsConfig.FirstOrDefault(x =>
                    x != null && NormalizeKeyPart(x.WindowTitle) == normalizedTitle
                );
                if (existingConfig != null && existingConfig.ConfigKey != configKey)
                {
                    existingConfig.ConfigKey = configKey;
                    shouldPersistConfig = true;
                }
            }

            if (existingConfig == null)
                return null;

            if (shouldPersistConfig)
            {
                MarkConfigChangedLocked();
                (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
            }

            persistedConfig = existingConfig.Clone();
            persistedConfig.WindowId = windowConfig.WindowId;
            persistedConfig.ProcessName = windowConfig.ProcessName;
            persistedConfig.ConfigKey = configKey;
        }

        if (snapshot is not null)
            WriteConfigAtomicIfCurrent(snapshot, snapshotVersion);

        return persistedConfig;
    }

    public T ReadConfig<T>(Func<ConfigFile, T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        lock (_syncRoot)
        {
            return reader(CloneConfig(_config));
        }
    }

    public void UpdateConfig(Action<ConfigFile> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            update(_config);
            _config = NormalizeConfig(_config);
            MarkConfigChangedLocked();
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        WriteConfigAtomicIfCurrent(snapshot, snapshotVersion);
    }

    /// <summary>
    /// Asynchronously updates and persists the user settings.
    /// </summary>
    /// <param name="update">Mutation to apply to the in-memory configuration.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    public async Task UpdateConfigAsync(
        Action<ConfigFile> update,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(update);

        ConfigFile snapshot;
        long snapshotVersion;
        lock (_syncRoot)
        {
            update(_config);
            _config = NormalizeConfig(_config);
            MarkConfigChangedLocked();
            (snapshot, snapshotVersion) = CreatePersistSnapshotLocked();
        }

        await WriteConfigAtomicIfCurrentAsync(snapshot, snapshotVersion, cancellationToken)
            .ConfigureAwait(false);
    }

    private void WriteUserSettingsLocked()
    {
        WriteConfigAtomicLocked(_config);
    }

    private (ConfigFile Snapshot, long Version) CreatePersistSnapshotLocked()
    {
        return (CloneConfig(_config), _configVersion);
    }

    private void MarkConfigChangedLocked()
    {
        _configVersion++;
    }

    private void RecordReadFailureLocked(Exception exception, string reason)
    {
        string exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        _lastReadFailure = new ConfigLoadFailure(reason, exceptionType, DefaultsRestored: true);
        Trace.TraceError(
            $"[Config] Failed to read user settings; defaults were restored. Reason={reason}; ExceptionType={exceptionType}"
        );
    }

    private void WriteConfigAtomicLocked(ConfigFile config)
    {
        WriteConfigAtomic(_filePath, CloneConfig(config));
    }

    private void WriteConfigAtomicIfCurrent(ConfigFile config, long configVersion)
    {
        _fileWriteGate.Wait();
        try
        {
            if (Interlocked.Read(ref _configVersion) != configVersion)
                return;

            WriteConfigAtomic(_filePath, config);
        }
        finally
        {
            _fileWriteGate.Release();
        }
    }

    private static void WriteConfigAtomic(string filePath, ConfigFile config)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = CreateTempConfigPath(filePath);
        try
        {
            File.WriteAllText(tempPath, SerializeConfig(config));
            ReplaceConfigFile(tempPath, filePath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private async Task WriteConfigAtomicAsync(
        ConfigFile config,
        CancellationToken cancellationToken
    )
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = CreateTempConfigPath(_filePath);
        try
        {
            string json = SerializeConfig(config);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceConfigFile(tempPath, _filePath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private async Task WriteConfigAtomicIfCurrentAsync(
        ConfigFile config,
        long configVersion,
        CancellationToken cancellationToken
    )
    {
        await _fileWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Interlocked.Read(ref _configVersion) != configVersion)
                return;

            await WriteConfigAtomicAsync(config, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileWriteGate.Release();
        }
    }

    private static string CreateTempConfigPath(string filePath)
    {
        return $"{filePath}.{Guid.NewGuid():N}.tmp";
    }

    private static string SerializeConfig(ConfigFile config)
    {
        return JsonConvert.SerializeObject(config, Formatting.Indented);
    }

    private static void ReplaceConfigFile(string tempPath, string filePath)
    {
        File.Move(tempPath, filePath, overwrite: true);
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            Trace.TraceWarning(
                "[Config] Failed to delete temporary settings file after atomic write."
            );
        }
    }

    private static ConfigFile NormalizeConfig(ConfigFile config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.WhitelistPrefixes ??= new List<string>();
        config.BlacklistPrefixes ??= new List<string>();
        config.SentryDsn ??= string.Empty;
        if (!Guid.TryParse(config.TelemetryUserId, out _))
            config.TelemetryUserId = Guid.NewGuid().ToString("D");

        config.FloatingWindowsConfig = (config.FloatingWindowsConfig ?? [])
            .Where(windowConfig => windowConfig is not null)
            .Select(windowConfig =>
            {
                WindowConfig clone = windowConfig!.Clone();
                if (string.IsNullOrWhiteSpace(clone.ConfigKey))
                    clone.ConfigKey = CreateConfigKey(clone);
                return clone;
            })
            .Cast<WindowConfig?>()
            .ToList();

        config.WindowKeybindTargets = KeybindCatalogBuilder
            .Build(config.WindowKeybindTargets)
            .Targets.Select(KeybindCatalogBuilder.CloneTarget)
            .ToList();

        return config;
    }

    private static ConfigFile CloneConfig(ConfigFile config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new ConfigFile
        {
            ResizeWindows = config.ResizeWindows,
            MoveWindows = config.MoveWindows,
            StartMinimized = config.StartMinimized,
            SentryDsn = config.SentryDsn,
            TelemetryUserId = config.TelemetryUserId,
            UseFixedWindowSize = config.UseFixedWindowSize,
            FocusOnHover = config.FocusOnHover,
            WindowWidth = config.WindowWidth,
            WindowHeight = config.WindowHeight,
            PreviewHighlightColor = config.PreviewHighlightColor,
            EnablePreviews = config.EnablePreviews,
            WhitelistPrefixes = config.WhitelistPrefixes.ToList(),
            BlacklistPrefixes = config.BlacklistPrefixes.ToList(),
            FloatingWindowsConfig = config
                .FloatingWindowsConfig.Where(windowConfig => windowConfig is not null)
                .Select(windowConfig => (WindowConfig?)windowConfig!.Clone())
                .ToList(),
            WindowKeybindTargets = config
                .WindowKeybindTargets.Select(KeybindCatalogBuilder.CloneTarget)
                .ToList(),
        };
    }

    private static string CreateConfigKey(WindowConfig windowConfig)
    {
        string title = NormalizeKeyPart(windowConfig.WindowTitle);
        string processName = NormalizeKeyPart(windowConfig.ProcessName);
        if (string.IsNullOrWhiteSpace(processName))
            return title;
        return $"{processName}|{title}";
    }

    private static string NormalizeKeyPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Describes a sanitized configuration load failure.
    /// </summary>
    /// <param name="Reason">Stable failure reason safe for logs and telemetry.</param>
    /// <param name="ExceptionType">Exception type name without file contents or user data.</param>
    /// <param name="DefaultsRestored">Whether defaults were written after the failure.</param>
    public sealed record ConfigLoadFailure(
        string Reason,
        string ExceptionType,
        bool DefaultsRestored
    );
}
