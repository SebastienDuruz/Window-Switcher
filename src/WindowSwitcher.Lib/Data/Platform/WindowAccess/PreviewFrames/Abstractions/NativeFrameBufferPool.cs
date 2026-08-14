using System.Runtime.InteropServices;

namespace WindowSwitcher.Lib.Data.Platform.WindowAccess.PreviewFrames.Abstractions;

internal sealed class NativeFrameBufferPool(int maximumBuffers) : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly List<Entry> _entries = [];
    private bool _disposed;

    internal Lease? TryRent(int length)
    {
        if (length <= 0)
            return null;

        lock (_syncRoot)
        {
            if (_disposed)
                return null;

            Entry? entry = _entries.FirstOrDefault(candidate => !candidate.IsLeased);
            if (entry is null)
            {
                if (_entries.Count >= maximumBuffers)
                    return null;
                entry = new Entry();
                _entries.Add(entry);
            }

            if (entry.Capacity < length)
            {
                if (entry.Pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(entry.Pointer);
                entry.Pointer = Marshal.AllocHGlobal(length);
                if (entry.Pointer == IntPtr.Zero)
                    return null;
                entry.Capacity = length;
            }

            entry.IsLeased = true;
            return new Lease(this, entry, length);
        }
    }

    private void Return(Entry entry)
    {
        lock (_syncRoot)
        {
            entry.IsLeased = false;
            if (!_disposed || entry.Pointer == IntPtr.Zero)
                return;
            Marshal.FreeHGlobal(entry.Pointer);
            entry.Pointer = IntPtr.Zero;
            entry.Capacity = 0;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (Entry entry in _entries.Where(candidate => !candidate.IsLeased))
            {
                if (entry.Pointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(entry.Pointer);
                entry.Pointer = IntPtr.Zero;
                entry.Capacity = 0;
            }
        }
    }

    internal sealed class Entry
    {
        internal IntPtr Pointer;
        internal int Capacity;
        internal bool IsLeased;
    }

    internal sealed class Lease : IDisposable
    {
        private NativeFrameBufferPool? _owner;
        private readonly Entry _entry;

        internal Lease(NativeFrameBufferPool owner, Entry entry, int length)
        {
            _owner = owner;
            _entry = entry;
            Length = length;
        }

        internal IntPtr Pointer => _entry.Pointer;
        internal int Length { get; }

        public void Dispose()
        {
            NativeFrameBufferPool? owner = Interlocked.Exchange(ref _owner, null);
            owner?.Return(_entry);
        }
    }
}
