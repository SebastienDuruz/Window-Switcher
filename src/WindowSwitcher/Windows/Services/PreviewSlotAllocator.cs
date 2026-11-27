using System;
using System.Collections.Generic;

namespace WindowSwitcher.Windows.Services;

/// <summary>
/// Hands out the lowest free slot to each preview without a saved position, and frees
/// it when the preview closes.
/// </summary>
internal sealed class PreviewSlotAllocator
{
    private readonly Dictionary<string, int> _slotsByWindowId = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the slot of <paramref name="windowId"/>, assigning the lowest free one
    /// on the first call.
    /// </summary>
    public int Acquire(string windowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);

        if (_slotsByWindowId.TryGetValue(windowId, out int existingSlot))
            return existingSlot;

        var usedSlots = new HashSet<int>(_slotsByWindowId.Values);
        int slot = 0;
        while (usedSlots.Contains(slot))
            slot++;

        _slotsByWindowId[windowId] = slot;
        return slot;
    }

    public void Release(string windowId)
    {
        if (!string.IsNullOrWhiteSpace(windowId))
            _slotsByWindowId.Remove(windowId);
    }

    public void Clear()
    {
        _slotsByWindowId.Clear();
    }
}
