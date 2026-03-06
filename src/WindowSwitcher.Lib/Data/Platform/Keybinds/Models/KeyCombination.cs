using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WindowSwitcher.Lib.Data.Platform.Keybinds.Utilities;

namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Structured key combination composed of modifiers and one primary key.
/// </summary>
public sealed class KeyCombination : IEquatable<KeyCombination>
{
    /// <summary>
    /// Gets or sets whether Ctrl is part of the combination.
    /// </summary>
    public bool Ctrl { get; set; }

    /// <summary>
    /// Gets or sets whether Alt is part of the combination.
    /// </summary>
    public bool Alt { get; set; }

    /// <summary>
    /// Gets or sets whether Shift is part of the combination.
    /// </summary>
    public bool Shift { get; set; }

    /// <summary>
    /// Gets or sets whether Meta/Win is part of the combination.
    /// </summary>
    public bool Meta { get; set; }

    /// <summary>
    /// Gets or sets the primary non-modifier key.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public KeybindPrimaryKey Key { get; set; }

    /// <summary>
    /// Returns a detached copy.
    /// </summary>
    public KeyCombination Clone()
    {
        return new KeyCombination
        {
            Ctrl = Ctrl,
            Alt = Alt,
            Shift = Shift,
            Meta = Meta,
            Key = Key,
        };
    }

    /// <inheritdoc />
    public bool Equals(KeyCombination? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null)
            return false;

        return Ctrl == other.Ctrl
            && Alt == other.Alt
            && Shift == other.Shift
            && Meta == other.Meta
            && Key == other.Key;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as KeyCombination);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Ctrl, Alt, Shift, Meta, Key);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return KeyCombinationParser.ToCanonicalString(this);
    }
}
