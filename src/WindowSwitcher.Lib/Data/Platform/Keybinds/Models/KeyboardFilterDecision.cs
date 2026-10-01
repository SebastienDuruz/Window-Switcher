namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Immutable decision produced by the keyboard filter for one captured event.
/// </summary>
public sealed class KeyboardFilterDecision
{
    private static readonly KeyboardFilterDecision ForwardDecision = new(
        KeyboardEventRouting.Forward
    );
    private static readonly KeyboardFilterDecision ConsumeDecision = new(
        KeyboardEventRouting.Consume
    );

    private KeyboardFilterDecision(
        KeyboardEventRouting routing,
        KeyCombination? matchedCombination = null,
        string? matchedTargetId = null
    )
    {
        Routing = routing;
        MatchedCombination = matchedCombination;
        MatchedTargetId = matchedTargetId;
    }

    /// <summary>
    /// Gets how the current event must be routed.
    /// </summary>
    public KeyboardEventRouting Routing { get; }

    /// <summary>
    /// Gets the matched combination when the current event triggered a binding.
    /// </summary>
    public KeyCombination? MatchedCombination { get; }

    /// <summary>
    /// Gets the matched target id when the current event triggered a binding.
    /// </summary>
    public string? MatchedTargetId { get; }

    /// <summary>
    /// Creates a forwarding decision.
    /// </summary>
    public static KeyboardFilterDecision Forward()
    {
        return ForwardDecision;
    }

    /// <summary>
    /// Creates a consume decision.
    /// </summary>
    public static KeyboardFilterDecision Consume(
        KeyCombination? matchedCombination = null,
        string? matchedTargetId = null
    )
    {
        if (matchedCombination is null && string.IsNullOrWhiteSpace(matchedTargetId))
            return ConsumeDecision;

        return new KeyboardFilterDecision(
            KeyboardEventRouting.Consume,
            matchedCombination: matchedCombination?.Clone(),
            matchedTargetId: matchedTargetId
        );
    }
}
