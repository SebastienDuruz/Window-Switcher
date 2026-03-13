namespace WindowSwitcher.Lib.Data.Platform.Keybinds.Models;

/// <summary>
/// Immutable decision produced by the keyboard filter for one captured event.
/// </summary>
public sealed class KeyboardFilterDecision
{
    private static readonly KeyboardFilterDecision ForwardDecision = new(
        KeyboardEventRouting.Forward
    );
    private static readonly KeyboardFilterDecision BufferDecision = new(KeyboardEventRouting.Buffer);
    private static readonly KeyboardFilterDecision ConsumeDecision = new(
        KeyboardEventRouting.Consume
    );

    private KeyboardFilterDecision(
        KeyboardEventRouting routing,
        bool flushBufferedEvents = false,
        bool forwardCurrentEventViaForwarder = false,
        bool discardBufferedEvents = false,
        KeyCombination? matchedCombination = null,
        string? matchedTargetId = null)
    {
        Routing = routing;
        FlushBufferedEvents = flushBufferedEvents;
        ForwardCurrentEventViaForwarder = forwardCurrentEventViaForwarder;
        DiscardBufferedEvents = discardBufferedEvents;
        MatchedCombination = matchedCombination;
        MatchedTargetId = matchedTargetId;
    }

    /// <summary>
    /// Gets how the current event must be routed.
    /// </summary>
    public KeyboardEventRouting Routing { get; }

    /// <summary>
    /// Gets whether any backend-side buffered prefix events must be forwarded before the current event.
    /// </summary>
    public bool FlushBufferedEvents { get; }

    /// <summary>
    /// Gets whether the current event must be forwarded through the backend replay mechanism rather than native propagation.
    /// </summary>
    public bool ForwardCurrentEventViaForwarder { get; }

    /// <summary>
    /// Gets whether backend-side buffered events must be dropped because they belong to a matched binding.
    /// </summary>
    public bool DiscardBufferedEvents { get; }

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
    public static KeyboardFilterDecision Forward(
        bool flushBufferedEvents = false,
        bool forwardCurrentEventViaForwarder = false)
    {
        if (!flushBufferedEvents && !forwardCurrentEventViaForwarder)
            return ForwardDecision;

        return new KeyboardFilterDecision(
            KeyboardEventRouting.Forward,
            flushBufferedEvents: flushBufferedEvents,
            forwardCurrentEventViaForwarder: forwardCurrentEventViaForwarder
        );
    }

    /// <summary>
    /// Creates a buffering decision.
    /// </summary>
    public static KeyboardFilterDecision Buffer()
    {
        return BufferDecision;
    }

    /// <summary>
    /// Creates a consume decision.
    /// </summary>
    public static KeyboardFilterDecision Consume(
        bool discardBufferedEvents = false,
        KeyCombination? matchedCombination = null,
        string? matchedTargetId = null)
    {
        if (!discardBufferedEvents && matchedCombination is null && string.IsNullOrWhiteSpace(matchedTargetId))
            return ConsumeDecision;

        return new KeyboardFilterDecision(
            KeyboardEventRouting.Consume,
            discardBufferedEvents: discardBufferedEvents,
            matchedCombination: matchedCombination?.Clone(),
            matchedTargetId: matchedTargetId
        );
    }
}
